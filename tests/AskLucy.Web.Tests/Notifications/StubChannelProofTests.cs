using System.Reflection;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Domain.Notifications;
using AskLucy.Infrastructure.Notifications;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>A test-only channel. It is deliberately not a member of <see cref="NotificationChannel"/>: see <see cref="StubChannelProofTests"/>.</summary>
internal static class StubChannel
{
    /// <summary>A value the production enum doesn't define, so adding the stub changes no production type.</summary>
    public const NotificationChannel Value = (NotificationChannel)100;
}

/// <summary>The stub channel's sender: records every context it is handed and reports success.</summary>
internal sealed class RecordingStubChannelSender : INotificationChannelSender
{
    private readonly List<DeliveryContext> _sent = [];

    public NotificationChannel Channel => StubChannel.Value;

    public IReadOnlyList<DeliveryContext> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public Task<ChannelSendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken)
    {
        context.Progress?.MarkTransmissionStarted();
        lock (_sent)
        {
            _sent.Add(context);
        }

        return Task.FromResult(new ChannelSendResult(ChannelSendOutcome.Sent, null, null, "stub accepted"));
    }
}

/// <summary>The only wiring a new channel needs: one sender, and the singleton marker that tells the registry it exists (FR-060).</summary>
public sealed class StubChannelFactory : CustomWebApplicationFactory
{
    internal RecordingStubChannelSender Sender { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<INotificationChannelSender>(Sender);
            services.AddSingleton(new RegisteredChannelSender(StubChannel.Value));
        });
    }
}

/// <summary>
/// specs/067 T225, SC-012: "adding a stub test channel in a proof exercise requires no changes to any module that
/// requests notifications or to routing rules" (research R7, FR-060).
/// <para>
/// <b>Why not a real <c>NotificationChannel</c> value and a catalogue entry.</b> The task text suggests registering a
/// <c>TestChannelSender</c> plus a test catalogue entry and template. <see cref="NotificationTypeCatalog"/> is a
/// frozen, code-owned static (R7), and the dispatcher, delivery processor and router all read it, so a test can't add an
/// entry to the production catalogue without mutating global state that every other test host shares; and adding a value
/// to the production <see cref="NotificationChannel"/> enum just for a test would itself be the "change" SC-012 forbids
/// us to need. So the stub uses an undefined enum value (<see cref="StubChannel.Value"/>, 100: the delivery's channel
/// column is stored as a string, so it round-trips) and a <c>with</c>-copy of a real catalogue definition that declares
/// the stub channel. Everything between the request and the sender is the production code, untouched:
/// </para>
/// <list type="number">
/// <item><description><see cref="INotificationPublisher"/> and <see cref="NotificationRequest"/> carry no channel (reflection check), so emitters can't depend on one.</description></item>
/// <item><description>The real <see cref="NotificationRouter"/> routes to the stub as soon as the registry lists it, and skips it (channel disabled) when it has no sender.</description></item>
/// <item><description>The real <see cref="INotificationChannelRegistry"/> in the host lists the stub purely because a sender was registered.</description></item>
/// <item><description>The real <see cref="NotificationMaterializer"/> builds a pending stub delivery, and the real <see cref="DeliveryProcessor"/> hands it to the stub sender and records it sent.</description></item>
/// </list>
/// What this does not exercise is the dispatcher's lookup of a type in the production catalogue, which by design only
/// knows the channels the type declares; a new channel ships with the catalogue edits that opt types into it.
/// </summary>
public sealed class StubChannelProofTests(StubChannelFactory factory) : IClassFixture<StubChannelFactory>
{
    private const string WorkerId = "stub-channel-proof-worker";

    private static NotificationTypeDefinition StubbedWorkflowFailed() =>
        NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed) with
        {
            Channels = new Dictionary<NotificationChannel, ChannelDefault> { [StubChannel.Value] = ChannelDefault.On },
        };

    [Fact]
    public void NoEmitterFacingType_KnowsAboutChannels()
    {
        var emitterFacing = new[]
        {
            typeof(INotificationPublisher), typeof(NotificationRequest), typeof(RelatedItem), typeof(NotificationRecipient),
        };

        var offenders = emitterFacing
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m switch
            {
                PropertyInfo p => p.PropertyType == typeof(NotificationChannel),
                FieldInfo f => f.FieldType == typeof(NotificationChannel),
                MethodBase mb => mb.GetParameters().Any(p => p.ParameterType == typeof(NotificationChannel)),
                _ => false,
            })
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        offenders.Should().BeEmpty("emitters say what happened, never how to deliver it (FR-002), so a new channel can't touch them");
    }

    [Fact]
    public void TheHostRegistry_ListsTheStubChannel_BecauseASenderIsRegistered_AndNothingElse()
    {
        using var scope = factory.Services.CreateScope();

        var available = scope.ServiceProvider.GetRequiredService<INotificationChannelRegistry>().AvailableChannels;

        available.Should().Contain([NotificationChannel.InApp, NotificationChannel.Email, StubChannel.Value]);
        scope.ServiceProvider.GetServices<INotificationChannelSender>().Select(s => s.Channel)
            .Should().Contain(StubChannel.Value, "the delivery processor finds senders by channel");
    }

    [Fact]
    public void TheUnchangedRouter_RoutesToTheStub_WhenListed_AndSkipsItWhenItHasNoSender()
    {
        var definition = StubbedWorkflowFailed();
        var recipient = new RecipientRoutingState(HasVerifiedEmail: true);

        var withSender = NotificationRouter.Route(definition, recipient, [], new HashSet<NotificationChannel> { NotificationChannel.InApp, StubChannel.Value }, isCritical: false);
        var withoutSender = NotificationRouter.Route(definition, recipient, [], new HashSet<NotificationChannel> { NotificationChannel.InApp }, isCritical: false);

        withSender.Should().ContainSingle().Which.Should().Be(ChannelDecision.Send(StubChannel.Value));
        withoutSender.Should().ContainSingle().Which.Should().Be(ChannelDecision.Skip(StubChannel.Value, DeliverySkipReason.ChannelDisabled));
    }

    [Fact]
    public async Task AStubDelivery_MaterializedByTheRealMaterializer_IsSentByTheRealDeliveryProcessor_ThroughTheStubSender()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = await SeedUserAsync();
        try
        {
            var definition = StubbedWorkflowFailed();
            var now = DateTime.UtcNow;
            var outbox = NotificationOutboxEvent.Create(
                definition.Key, NotificationRecipientJson.Serialize(new NotificationRecipient.User(userId)), "{}", $"corr-{Guid.NewGuid():N}", now, eventKey: $"stub-proof:{Guid.NewGuid():N}");
            outbox.Claim(WorkerId, now.AddMinutes(2), now);
            outbox.Complete(OutboxEventOutcome.Materialized, now); // already handled: the host's own dispatcher must not turn it into a second notification

            Guid deliveryId;
            Notification notification;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var materializer = scope.ServiceProvider.GetRequiredService<NotificationMaterializer>();
                notification = await materializer.MaterializeAsync(
                    definition, outbox, new Dictionary<string, string?>(), new MaterializationTarget(userId, RecipientKind.User, null, "Stub Proof", HasVerifiedEmail: true, []), ct);

                var delivery = notification.Deliveries.Should().ContainSingle().Subject;
                delivery.Channel.Should().Be(StubChannel.Value);
                delivery.Status.Should().Be(DeliveryStatus.Pending, "the materializer queued it for its channel's sender");

                delivery.MarkSending(WorkerId, now.AddMinutes(2), now); // claimed by this test, so no other host's worker takes it
                deliveryId = delivery.Id;

                var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
                db.NotificationOutboxEvents.Add(outbox);
                db.Notifications.Add(notification);
                await db.SaveChangesAsync(ct);
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var outcome = await scope.ServiceProvider.GetRequiredService<DeliveryProcessor>().ProcessAsync(deliveryId, WorkerId, ct);
                outcome.Should().Be(DeliveryProcessOutcome.Completed);
            }

            var sent = factory.Sender.Sent.Should().ContainSingle(c => c.DeliveryId == deliveryId).Subject;
            sent.Definition.Key.Should().Be(NotificationTypeKeys.WorkflowExecutionFailed);
            sent.NotificationId.Should().Be(notification.Id);

            await using var verify = factory.Services.CreateAsyncScope();
            var stored = await verify.ServiceProvider.GetRequiredService<AskLucyDbContext>().Set<NotificationDelivery>().AsNoTracking()
                .SingleAsync(d => d.Id == deliveryId, ct);
            stored.Channel.Should().Be(StubChannel.Value);
            stored.Status.Should().Be(DeliveryStatus.Sent);
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    private async Task<string> SeedUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"stub-channel-{Guid.NewGuid():N}@tests.asklucy.io";
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, CreatedAtUtc = DateTime.UtcNow };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task CleanupAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        await db.Notifications.IgnoreQueryFilters().Where(n => n.RecipientUserId == userId).ExecuteDeleteAsync();
        await db.NotificationOutboxEvents.Where(e => e.RecipientJson.Contains(userId)).ExecuteDeleteAsync();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByIdAsync(userId) is { } user)
        {
            await users.DeleteAsync(user);
        }
    }
}
