using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Notifications.Workers;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>
/// One real host for the fault-injection run. Leases are one minute (the shortest the options allow), so
/// the test can wait out a killed worker's lease; every other setting is the product default.
/// </summary>
public sealed class DeliveryFaultInjectionFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:Dispatch:LeaseMinutes"] = "1",
            ["Notifications:Dispatch:IdlePollIntervalSeconds"] = "1",
        }));
    }
}

/// <summary>
/// Runs alone. Every host in the process runs a delivery worker against the one shared database, and this test
/// stops workers and scripts faults for the shared fake SMTP server; another test's host picking up one of its
/// deliveries, or its own deliveries being stopped by a fault meant for someone else, would make both unreliable.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DeliveryFaultInjectionTestGroup
{
    public const string Name = "Notification delivery fault injection";
}

/// <summary>
/// T108 — specs/067 SC-003 and quickstart §4. Drives 1,000 notifications through the real pipeline (the
/// outbox, the dispatcher, the delivery workers, the real database) against a fake SMTP server, while
/// injecting the faults the spec names: (a) a worker killed mid-send, (b) SMTP 4xx replies and dropped
/// connections, a rejected recipient, (c) concurrent workers, and (d) an emitting handler that throws after
/// its save and is run again. Nothing may be lost, no Message-ID may be accepted twice, and a send whose
/// outcome is unknown must be recorded as ambiguous and left alone.
/// </summary>
[Collection(DeliveryFaultInjectionTestGroup.Name)]
public sealed partial class DeliveryFaultInjectionTests(DeliveryFaultInjectionFactory factory) : IClassFixture<DeliveryFaultInjectionFactory>
{
    private const int UserCount = 25;

    // 40 per user is the 1,000 of SC-003; DELIVERY_FAULT_EVENTS_PER_USER (a multiple of 10) shrinks the run for local debugging.
    private static readonly int EventsPerUser = int.TryParse(Environment.GetEnvironmentVariable("DELIVERY_FAULT_EVENTS_PER_USER"), out var perUser) && perUser >= 10 ? perUser : 40;
    private static readonly int Total = UserCount * EventsPerUser;

    // Which fault a recipient gets, by index.
    private static readonly Range TemporaryReplies = 0..5;     // two 450s, then accepted
    private static readonly Range DroppedConnections = 5..10;  // one dropped connection, then accepted
    private const int MaxKills = 6;                            // sends that kill the workers, per run, at most
    private const int RejectedRecipient = 10;                  // 550, permanent
    private static readonly Range KilledWorker = 11..14;       // the worker dies mid-send on the first attempt

    [GeneratedRegex(@"^<(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})@bimcatalyst\.com>$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageIdPattern();

    [GeneratedRegex(@"^fault-(?<run>[0-9a-f]+)-(?<index>\d+)@tests\.asklucy\.io$", RegexOptions.CultureInvariant)]
    private static partial Regex RecipientPattern();

    [Fact]
    public async Task ThousandDeliveries_UnderEveryInjectedFault_LoseNothing_DuplicateNothing_AndRecordAmbiguousSendsWithoutResendingThem()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N")[..8];
        var sender = ScriptableEmailSender.Shared;
        sender.Reset();
        var users = await SeedUsersAsync(run);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            var kills = 0;
            sender.Policy = (message, attempt) =>
            {
                var fault = PolicyFor(run, message, attempt, Volatile.Read(ref kills));
                if (fault == EmailFault.CrashMidSend)
                {
                    Interlocked.Increment(ref kills);
                }

                return fault;
            };

            // (c) Two real delivery workers run side by side: the host's own and a second instance.
            // (a) A send that "crashes" stops both mid-send, as a killed process would, and the test then
            // restarts them, as a supervisor would. StopAsync is the real shutdown path, so what it leaves
            // behind is exactly what a restart leaves behind.
            var workers = new List<NotificationDeliveryWorker>
            {
                factory.Services.GetServices<IHostedService>().OfType<NotificationDeliveryWorker>().Single(),
                ActivatorUtilities.CreateInstance<NotificationDeliveryWorker>(factory.Services),
            };
            var pendingStops = new System.Collections.Concurrent.ConcurrentQueue<Task>();
            sender.OnCrash = () =>
            {
                foreach (var worker in workers)
                {
                    pendingStops.Enqueue(worker.StopAsync(CancellationToken.None));
                }
            };

            await workers[1].StartAsync(ct);
            var supervisor = SuperviseAsync(workers, pendingStops, stop.Token);

            // (d) Each handler commits its events, "throws", and is run again: the second publish must collapse.
            await PublishAllAsync(users, run, ct);

            await WaitForAllDeliveriesToSettleAsync(users, expectNotifications: Total, ct);

            // Wait out any killed worker's lease, then let the sweeper record those sends as ambiguous.
            await SweepAbandonedSendsAsync(users, ct);

            await stop.CancelAsync();
            var restarts = await supervisor;
            await workers[1].StopAsync(CancellationToken.None);
            Volatile.Read(ref kills).Should().BeGreaterThan(0,
                "the run is meant to kill the workers mid-send at least once. {0}",
                $"Server saw {sender.Sent.Count} sends / {sender.Accepted.Count} accepted. Deliveries: {await BreakdownAsync(users)}");
            restarts.Should().BeGreaterThan(0, "every kill is followed by a restart");

            await AssertNothingLostOrDuplicatedAsync(users, run, sender, ct);
        }
        finally
        {
            await stop.CancelAsync();
            sender.Reset();
            await CleanupAsync(users, run);
        }
    }

    // ---- fault policy ----

    private static EmailFault PolicyFor(string run, AskLucy.Application.Abstractions.EmailMessage message, int attempt, int killsSoFar)
    {
        var match = RecipientPattern().Match(message.To);
        if (!match.Success || match.Groups["run"].Value != run)
        {
            return EmailFault.None; // someone else's delivery, picked up by one of the shared workers
        }

        var index = int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (InRange(TemporaryReplies, index))
        {
            return attempt <= 2 ? EmailFault.Temporary450 : EmailFault.None;
        }

        if (InRange(DroppedConnections, index))
        {
            return attempt == 1 ? EmailFault.DroppedConnection : EmailFault.None;
        }

        if (index == RejectedRecipient)
        {
            return EmailFault.RecipientRejected550;
        }

        if (!InRange(KilledWorker, index))
        {
            return EmailFault.None;
        }

        // The workers are killed mid-send on the first attempt, up to MaxKills times; after that these
        // recipients are sent normally.
        return attempt == 1 && killsSoFar < MaxKills ? EmailFault.CrashMidSend : EmailFault.None;
    }

    private static bool InRange(Range range, int index) => index >= range.Start.Value && index < range.End.Value;

    // ---- workers ----

    /// <summary>Restarts the workers after each kill, until told to stop. Returns how many times it restarted them.</summary>
    private static async Task<int> SuperviseAsync(
        IReadOnlyList<NotificationDeliveryWorker> workers, System.Collections.Concurrent.ConcurrentQueue<Task> pendingStops, CancellationToken stop)
    {
        var restarts = 0;
        while (!stop.IsCancellationRequested)
        {
            if (pendingStops.IsEmpty)
            {
                await Task.Delay(50, CancellationToken.None);
                continue;
            }

            // One kill stops every worker; wait for all of them, then bring them back.
            while (pendingStops.TryDequeue(out var stopping))
            {
                await stopping;
            }

            foreach (var worker in workers)
            {
                await worker.StartAsync(CancellationToken.None);
            }

            restarts++;
        }

        return restarts;
    }

    // ---- publishing ----

    private async Task PublishAllAsync(List<string> users, string run, CancellationToken ct)
    {
        for (var userIndex = 0; userIndex < users.Count; userIndex++)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var requests = Enumerable.Range(0, EventsPerUser).Select(j => RequestFor(users[userIndex], run, userIndex, j)).ToList();
            foreach (var request in requests)
            {
                publisher.Publish(request);
            }

            await unitOfWork.SaveChangesAsync(ct);

            // (d) The handler threw after its SaveChanges, so its caller ran it again with the same event keys.
            await using var retryScope = factory.Services.CreateAsyncScope();
            var retryPublisher = retryScope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            foreach (var request in requests.Where((_, j) => j % 10 == 0))
            {
                retryPublisher.Publish(request);
            }

            await retryScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        }
    }

    private static NotificationRequest RequestFor(string userId, string run, int userIndex, int j)
    {
        var key = $"fault:{run}:{userIndex}:{j}";
        return j % 2 == 0
            ? new NotificationRequest(
                NotificationTypeKeys.SecurityTwoFactorEnabled, new NotificationRecipient.User(userId),
                new Dictionary<string, string?> { ["changedAt"] = "2026-10-06 09:00 UTC" }, EventKey: key)
            : new NotificationRequest(
                NotificationTypeKeys.DocumentStorageLimitReached, new NotificationRecipient.User(userId),
                new Dictionary<string, string?> { ["usedStorage"] = "9.8 GB", ["storageLimit"] = "10 GB" }, EventKey: key);
    }

    // ---- waiting ----

    private async Task WaitForAllDeliveriesToSettleAsync(List<string> users, int expectNotifications, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(5);
        (int Notifications, int Waiting) last = (0, 0);
        while (DateTime.UtcNow < deadline)
        {
            last = await QueryAsync(async db =>
            {
                var notifications = await db.Notifications.IgnoreQueryFilters().CountAsync(n => n.RecipientUserId != null && users.Contains(n.RecipientUserId), ct);
                var waiting = await db.Set<NotificationDelivery>().CountAsync(d =>
                    db.Notifications.IgnoreQueryFilters().Any(n => n.Id == d.NotificationId && n.RecipientUserId != null && users.Contains(n.RecipientUserId))
                    && (d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Retrying), ct);
                return (notifications, waiting);
            });

            if (last.Notifications >= expectNotifications && last.Waiting == 0)
            {
                return;
            }

            await Task.Delay(500, ct);
        }

        var breakdown = await BreakdownAsync(users);

        throw new TimeoutException(
            $"After 5 minutes {last.Notifications} of {expectNotifications} notifications existed and {last.Waiting} deliveries were still pending or retrying. Deliveries: {breakdown}");
    }

    private Task<string> BreakdownAsync(List<string> users) => QueryAsync(async db => string.Join("; ", (await db.Set<NotificationDelivery>().AsNoTracking()
        .Where(d => db.Notifications.IgnoreQueryFilters().Any(n => n.Id == d.NotificationId && n.RecipientUserId != null && users.Contains(n.RecipientUserId)))
        .GroupBy(d => new { d.Status, d.FailureKind, d.AttemptCount, d.FailureReason })
        .Select(g => new { g.Key.Status, g.Key.FailureKind, g.Key.AttemptCount, g.Key.FailureReason, Count = g.Count() })
        .ToListAsync(CancellationToken.None)).Select(g => $"{g.Count}x {g.Status}/{g.FailureKind}/attempts={g.AttemptCount}/{g.FailureReason}")));

    private async Task SweepAbandonedSendsAsync(List<string> users, CancellationToken ct)
    {
        var sweeper = factory.Services.GetRequiredService<LeaseSweepService>();
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (DateTime.UtcNow < deadline)
        {
            var leftSending = await QueryAsync(db => db.Set<NotificationDelivery>().CountAsync(d =>
                d.Status == DeliveryStatus.Sending
                && db.Notifications.IgnoreQueryFilters().Any(n => n.Id == d.NotificationId && n.RecipientUserId != null && users.Contains(n.RecipientUserId)), ct));
            if (leftSending == 0)
            {
                return;
            }

            // A killed worker's lease is one minute; a live send never lasts that long here.
            await sweeper.SweepAsync(ct);
            await Task.Delay(2000, ct);
        }

        throw new TimeoutException("Deliveries were still Sending three minutes after the last worker was killed.");
    }

    // ---- assertions ----

    private async Task AssertNothingLostOrDuplicatedAsync(List<string> users, string run, ScriptableEmailSender sender, CancellationToken ct)
    {
        var rows = await QueryAsync(async db => await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Include(n => n.Deliveries)
            .Where(n => n.RecipientUserId != null && users.Contains(n.RecipientUserId))
            .ToListAsync(ct));

        // lost = 0: every event became exactly one notification, however often its handler ran (d).
        rows.Should().HaveCount(Total);
        rows.Select(n => n.EventKey).Should().OnlyHaveUniqueItems().And.HaveCount(Total);

        var emails = rows.Select(n => (Notification: n, Email: n.Deliveries.Single(d => d.Channel == NotificationChannel.Email))).ToList();
        emails.Should().OnlyContain(e => e.Email.Status != DeliveryStatus.Pending && e.Email.Status != DeliveryStatus.Retrying && e.Email.Status != DeliveryStatus.Sending,
            "every delivery reached a terminal state");
        emails.Should().NotContain(e => e.Email.Status == DeliveryStatus.DeadLettered, "no injected outage outlasts the retry schedule");

        var outboxByOutcome = await QueryAsync(async db => await db.NotificationOutboxEvents.AsNoTracking()
            .Where(e => e.EventKey != null && e.EventKey.StartsWith($"fault:{run}:"))
            .GroupBy(e => new { e.Status, e.Outcome }).Select(g => new { g.Key.Status, g.Key.Outcome, Count = g.Count() }).ToListAsync(ct));
        outboxByOutcome.Should().OnlyContain(g => g.Status == OutboxEventStatus.Completed, "every outbox event was processed");
        outboxByOutcome.Where(g => g.Outcome == OutboxEventOutcome.Materialized).Sum(g => g.Count).Should().Be(Total);
        outboxByOutcome.Where(g => g.Outcome == OutboxEventOutcome.Duplicate).Sum(g => g.Count).Should().Be(UserCount * (EventsPerUser / 10));

        // Each recipient's fate follows from the fault they were given.
        var byIndex = emails.GroupBy(e => IndexOf(e.Email, users, e.Notification)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var (index, group) in byIndex)
        {
            if (index == RejectedRecipient)
            {
                group.Should().OnlyContain(e => e.Email.Status == DeliveryStatus.Failed && e.Email.FailureKind == DeliveryFailureKind.Permanent && e.Email.AttemptCount == 1,
                    "a rejected recipient fails at once and is never retried");
            }
            else if (InRange(KilledWorker, index))
            {
                group.Should().OnlyContain(e => e.Email.Status == DeliveryStatus.Sent || (e.Email.Status == DeliveryStatus.Failed && e.Email.FailureKind == DeliveryFailureKind.AmbiguousOutcome));
            }
            else
            {
                group.Should().OnlyContain(e => e.Email.Status == DeliveryStatus.Sent, $"recipient {index}'s faults are all transient");
            }
        }

        // The outage recipients really did go through their retries.
        byIndex.Where(p => InRange(TemporaryReplies, p.Key)).SelectMany(p => p.Value).Should().OnlyContain(e => e.Email.AttemptCount == 3);
        byIndex.Where(p => InRange(DroppedConnections, p.Key)).SelectMany(p => p.Value).Should().OnlyContain(e => e.Email.AttemptCount == 2);

        // duplicates = 0: no Message-ID was accepted twice, and every accepted one is a delivery of this run.
        sender.Accepted.Should().OnlyHaveUniqueItems("a Message-ID accepted twice is a duplicate email");
        var acceptedIds = sender.Accepted.Select(id => Guid.Parse(MessageIdPattern().Match(id).Groups["id"].Value)).ToHashSet();
        sender.Accepted.Should().OnlyContain(id => MessageIdPattern().IsMatch(id), "the Message-ID is <{{deliveryId}}@{{domain}}> (R5)");

        var sent = emails.Where(e => e.Email.Status == DeliveryStatus.Sent).Select(e => e.Email.Id).ToHashSet();
        var ambiguous = emails.Where(e => e.Email.FailureKind == DeliveryFailureKind.AmbiguousOutcome).Select(e => e.Email.Id).ToHashSet();
        ambiguous.Should().NotBeEmpty("a worker was killed mid-send, so some sends are of unknown outcome");
        sent.Should().BeSubsetOf(acceptedIds);

        var reachedServer = sent.Union(ambiguous).ToHashSet();
        var neverAccepted = reachedServer.Where(id => !acceptedIds.Contains(id)).ToList();
        neverAccepted.Should().BeEmpty(
            "an ambiguous delivery is one whose send may have happened, but the fake server records a send before it can be interrupted. {0}",
            string.Join("; ", emails.Where(e => neverAccepted.Contains(e.Email.Id)).Select(e =>
                $"{e.Email.Id}: {e.Email.Status}/{e.Email.FailureKind} attempts={e.Email.AttemptCount} created={e.Email.CreatedAtUtc:O} lastAttempt={e.Email.LastAttemptAtUtc:O} recipient#{IndexOf(e.Email, users, e.Notification)} serverAttempts={sender.AttemptsFor($"<{e.Email.Id:D}@bimcatalyst.com>")}")));
        acceptedIds.Where(id => emails.Any(e => e.Email.Id == id)).Should().BeSubsetOf(reachedServer, "only sent and ambiguous deliveries ever reached the server");

        // An ambiguous send is listed for an administrator and never resent automatically.
        foreach (var email in emails.Where(e => ambiguous.Contains(e.Email.Id)))
        {
            email.Email.NextAttemptAtUtc.Should().BeNull();
            sender.AttemptsFor($"<{email.Email.Id:D}@bimcatalyst.com>").Should().Be(email.Email.AttemptCount, "no send was attempted after the killed one: an ambiguous delivery is not resent");
        }

        // The notification-level status follows the deliveries (R8): in-app copies were delivered at creation.
        rows.Where(n => n.ShowInCenter).Should().OnlyContain(n => n.Status == NotificationStatus.Delivered);
    }

    private static int IndexOf(NotificationDelivery delivery, List<string> users, Notification notification)
    {
        _ = delivery;
        return users.ToList().IndexOf(notification.RecipientUserId!);
    }

    // ---- data ----

    private async Task<List<string>> SeedUsersAsync(string run)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var ids = new List<string>(UserCount);
        for (var i = 0; i < UserCount; i++)
        {
            var email = $"fault-{run}-{i}@tests.asklucy.io";
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
            (await userManager.CreateAsync(user)).Succeeded.Should().BeTrue();
            ids.Add(user.Id);
        }

        return ids;
    }

    private async Task CleanupAsync(List<string> users, string run)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var ct = CancellationToken.None;

        await db.Notifications.IgnoreQueryFilters().Where(n => n.RecipientUserId != null && users.Contains(n.RecipientUserId)).ExecuteDeleteAsync(ct);
        await db.NotificationOutboxEvents.Where(e => e.EventKey != null && e.EventKey.StartsWith($"fault:{run}:")).ExecuteDeleteAsync(ct);

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var id in users)
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is not null)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }

    private async Task<T> QueryAsync<T>(Func<AskLucyDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AskLucyDbContext>());
    }
}
