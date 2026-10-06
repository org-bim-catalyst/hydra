using System.Reflection;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Processing;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Options;
using AskLucy.Domain.Notifications;
using AskLucy.Domain.OperationalFailures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>
/// The outbox relay end to end over in-memory fakes (T009): claim, materialize, commit, push,
/// and release with backoff. Each event runs in its own DI scope, as in production.
/// </summary>
public sealed class OutboxDispatchServiceTests : IDisposable
{
    private const string WorkerId = "worker-1";
    private const string UserId = "user-1";
    private const string EventKey = "document:doc-1:processing-failed";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeDatabase _db;
    private readonly FakeRecipientDirectory _directory = new();
    private readonly FakeRenderer _renderer = new();
    private readonly FakeRealtime _realtime;
    private readonly INotificationAccessCheck _documentAccess = Substitute.For<INotificationAccessCheck>();
    private readonly IOperationalFailureRecorder _failureRecorder = Substitute.For<IOperationalFailureRecorder>();
    private readonly FakeLogger<OutboxDispatchService> _dispatchLogger = new();
    private readonly FakeLogger<OutboxEventProcessor> _processorLogger = new();
    private readonly FakeLogger<NotificationMaterializer> _materializerLogger = new();
    private readonly FakeLogger<NotificationCreatedPusher> _pusherLogger = new();
    private readonly ServiceProvider _services;

    public OutboxDispatchServiceTests()
    {
        _db = new FakeDatabase(_time);
        _realtime = new FakeRealtime(_db);
        _directory.Accounts[UserId] = new NotificationRecipientInfo(UserId, "Layla", "layla@example.com", EmailConfirmed: true, IsActive: true);
        _documentAccess.ItemType.Returns("document");
        _documentAccess.CanAccessAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var channels = Substitute.For<INotificationChannelRegistry>();
        channels.AvailableChannels.Returns(new HashSet<NotificationChannel> { NotificationChannel.InApp, NotificationChannel.Email });
        var languages = Substitute.For<IEffectiveLanguageResolver>();
        languages.ResolveAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns("en");
        var links = Substitute.For<INotificationLinkBuilder>();
        links.BuildRelative(Arg.Any<NotificationTypeDefinition>(), Arg.Any<RelatedItem?>(), Arg.Any<Guid>()).Returns("/documents?documentId=doc-1");
        var preferences = Substitute.For<INotificationPreferenceRepository>();
        preferences.GetOverridesForUsersAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, IReadOnlyList<PreferenceOverride>>());

        _services = new ServiceCollection()
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(new NotificationsOptions()))
            .AddSingleton(_db)
            .AddScoped<FakeUnitOfWork>()
            .AddScoped<INotificationOutboxStore>(sp => sp.GetRequiredService<FakeUnitOfWork>())
            .AddScoped<INotificationRepository>(sp => sp.GetRequiredService<FakeUnitOfWork>())
            .AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<FakeUnitOfWork>())
            .AddSingleton<INotificationRecipientDirectory>(_directory)
            .AddSingleton(preferences)
            .AddSingleton(_documentAccess)
            .AddSingleton(channels)
            .AddSingleton(languages)
            .AddSingleton<INotificationTemplateRenderer>(_renderer)
            .AddSingleton(links)
            .AddSingleton<INotificationRealtimePublisher>(_realtime)
            .AddSingleton(_failureRecorder)
            .AddSingleton(Substitute.For<INotificationMetrics>())
            .AddSingleton<ILogger<OutboxDispatchService>>(_dispatchLogger)
            .AddSingleton<ILogger<OutboxEventProcessor>>(_processorLogger)
            .AddSingleton<ILogger<NotificationMaterializer>>(_materializerLogger)
            .AddSingleton<ILogger<NotificationCreatedPusher>>(_pusherLogger)
            .AddSingleton<NotificationMaterializer>()
            .AddScoped<NotificationCreatedPusher>()
            .AddScoped<OutboxEventProcessor>()
            .AddSingleton<OutboxDispatchService>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public void Dispose() => _services.Dispose();

    private OutboxDispatchService Dispatcher => _services.GetRequiredService<OutboxDispatchService>();

    private NotificationOutboxEvent Row(Guid id) => _db.OutboxRows.Single(e => e.Id == id);

    private Guid Enqueue(
        NotificationRecipient? recipient = null,
        string type = NotificationTypeKeys.DocumentProcessingFailed,
        string? eventKey = EventKey,
        string? relatedItemType = "document")
    {
        var ev = NotificationOutboxEvent.Create(
            type,
            NotificationRecipientJson.Serialize(recipient ?? new NotificationRecipient.User(UserId)),
            """{"documentName":"Tower A.pdf","failureSummary":"OCR timed out"}""",
            "corr-1",
            _time.GetUtcNow().UtcDateTime,
            eventKey,
            relatedItemType,
            "doc-1");
        _db.OutboxRows.Add(ev);
        return ev.Id;
    }

    [Fact]
    public async Task Event_MaterializesOneNotification_WithInAppDelivered_AndEmailPending()
    {
        var ev = Enqueue();

        var claimed = await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        claimed.Should().Be(1);
        Row(ev).Status.Should().Be(OutboxEventStatus.Completed);
        Row(ev).Outcome.Should().Be(OutboxEventOutcome.Materialized);

        var notification = _db.Committed.Should().ContainSingle().Subject;
        notification.RecipientUserId.Should().Be(UserId);
        notification.ShowInCenter.Should().BeTrue();
        notification.Title.Should().Be("Tower A.pdf failed");
        notification.ActionRoute.Should().Be("/documents?documentId=doc-1");
        notification.EventKey.Should().Be(EventKey);
        notification.SourceEventId.Should().Be(ev);
        notification.Deliveries.Should().ContainSingle(d => d.Channel == NotificationChannel.InApp && d.Status == DeliveryStatus.Delivered);
        notification.Deliveries.Should().ContainSingle(d => d.Channel == NotificationChannel.Email && d.Status == DeliveryStatus.Pending);
    }

    [Fact]
    public async Task Event_ForARecipientWithNoVerifiedEmail_SkipsTheEmail()
    {
        _directory.Accounts[UserId] = _directory.Accounts[UserId] with { EmailConfirmed = false };
        Enqueue();

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        _db.Committed.Single().Deliveries.Should().ContainSingle(d =>
            d.Channel == NotificationChannel.Email && d.Status == DeliveryStatus.Skipped && d.SkipReason == DeliverySkipReason.NoVerifiedAddress);
    }

    [Fact]
    public async Task Event_StandardVariables_AreFilledByTheHub()
    {
        Enqueue();

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        _renderer.LastVariables.Should().Contain("documentName", "Tower A.pdf")
            .And.Contain("recipientDisplayName", "Layla")
            .And.Contain("actionUrl", "/documents?documentId=doc-1")
            .And.Contain("occurredAt", "2026-09-28 10:00 UTC");
    }

    [Fact]
    public async Task DuplicateEventKey_MaterializesNothing()
    {
        Enqueue();
        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);
        var duplicate = Enqueue();
        _realtime.Pushes.Clear();

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        _db.Committed.Should().ContainSingle();
        Row(duplicate).Status.Should().Be(OutboxEventStatus.Completed);
        Row(duplicate).Outcome.Should().Be(OutboxEventOutcome.Duplicate);
        _realtime.Pushes.Should().BeEmpty();
    }

    [Fact]
    public async Task UsersRecipient_SkipsOnlyThoseAlreadyNotified()
    {
        _directory.Accounts["user-2"] = new NotificationRecipientInfo("user-2", "Omar", "omar@example.com", true, true);
        Enqueue();
        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        var ev = Enqueue(new NotificationRecipient.Users([UserId, "user-2", "user-2"]));
        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        Row(ev).Outcome.Should().Be(OutboxEventOutcome.Materialized);
        _db.Committed.Select(n => n.RecipientUserId).Should().BeEquivalentTo([UserId, "user-2"]);
    }

    [Fact]
    public async Task InactiveRecipient_GetsAHiddenNotification_WithEveryDeliveryCancelled()
    {
        _directory.Accounts[UserId] = _directory.Accounts[UserId] with { IsActive = false };
        Enqueue();

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        var notification = _db.Committed.Should().ContainSingle().Subject;
        notification.ShowInCenter.Should().BeFalse();
        notification.Deliveries.Should().HaveCount(2).And.OnlyContain(d => d.Status == DeliveryStatus.Cancelled);
        _renderer.Calls.Should().Be(0);
        _realtime.Pushes.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownRecipient_CreatesNothing_AndIsLogged()
    {
        var ev = Enqueue(new NotificationRecipient.User("ghost"));

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        _db.Committed.Should().BeEmpty();
        Row(ev).Outcome.Should().Be(OutboxEventOutcome.NoRecipient);
        _processorLogger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "UnknownRecipient" && r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AccessDenied_CreatesNothing_AndIsLogged()
    {
        _documentAccess.CanAccessAsync(UserId, "doc-1", Arg.Any<CancellationToken>()).Returns(false);
        var ev = Enqueue();

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        _db.Committed.Should().BeEmpty();
        Row(ev).Status.Should().Be(OutboxEventStatus.Completed);
        Row(ev).Outcome.Should().Be(OutboxEventOutcome.Rejected);
        _processorLogger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "AccessDenied");
        _realtime.Pushes.Should().BeEmpty();
    }

    [Fact]
    public async Task RelatedItemTypeWithNoAccessCheck_FailsClosed_AndReleasesTheEvent()
    {
        var ev = Enqueue(relatedItemType: "agent");

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        _db.Committed.Should().BeEmpty();
        Row(ev).Status.Should().Be(OutboxEventStatus.Pending);
        Row(ev).LastError.Should().Contain("agent");
        _dispatchLogger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "DispatchFailed" && r.Level == LogLevel.Error);
    }

    [Fact]
    public async Task RenderError_FailsTheInAppDelivery_AndIsLogged()
    {
        _renderer.Fail = true;
        Enqueue();

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        var notification = _db.Committed.Should().ContainSingle().Subject;
        notification.ShowInCenter.Should().BeFalse();
        notification.Deliveries.Should().ContainSingle(d =>
            d.Channel == NotificationChannel.InApp && d.Status == DeliveryStatus.Failed && d.FailureKind == DeliveryFailureKind.RenderError);
        _materializerLogger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "InAppRenderFailed" && r.Level == LogLevel.Error);
        _realtime.Pushes.Should().BeEmpty();
    }

    [Fact]
    public async Task RealtimePush_HappensOnlyAfterTheCommit()
    {
        Enqueue();

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        var push = _realtime.Pushes.Should().ContainSingle().Subject;
        push.UserId.Should().Be(UserId);
        push.WasCommitted.Should().BeTrue();
        push.UnreadCount.Should().Be(1);
        push.Item.Action.Should().Be(new NotificationActionDto("Open document", "/documents?documentId=doc-1"));
    }

    [Fact]
    public async Task FailedPush_IsLoggedAtWarning_AndTheEventStillCompletes()
    {
        _realtime.Fail = true;
        var ev = Enqueue();

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        Row(ev).Status.Should().Be(OutboxEventStatus.Completed);
        _db.Committed.Should().ContainSingle();
        _pusherLogger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "PushFailed" && r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task LostDeduplicationRace_CommitsNothing_PushesNothing_AndReleasesWithBackoff()
    {
        _db.ConflictOnSave = true;
        var ev = Enqueue();

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        _db.Committed.Should().BeEmpty();
        _realtime.Pushes.Should().BeEmpty();
        Row(ev).Status.Should().Be(OutboxEventStatus.Pending);
        Row(ev).Attempts.Should().Be(1);
        Row(ev).LeaseOwner.Should().BeNull();
        Row(ev).NextAttemptAtUtc.Should().Be(_time.GetUtcNow().UtcDateTime.AddSeconds(10));
        _failureRecorder.DidNotReceive().Record(Arg.Any<OperationalFailureReport>());
    }

    [Fact]
    public async Task ReleasedEvent_IsNotReclaimedBeforeItsBackoff()
    {
        _db.ConflictOnSave = true;
        Enqueue();
        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        _time.Advance(TimeSpan.FromSeconds(9));
        (await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None)).Should().Be(0);

        _time.Advance(TimeSpan.FromSeconds(1));
        (await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task AfterTheMaximumAttempts_TheEventFails_AndGoesOnTheOperationalFailureTrail()
    {
        _db.ConflictOnSave = true;
        var ev = Enqueue();

        for (var attempt = 1; attempt <= NotificationOutboxEvent.MaxDispatchAttempts; attempt++)
        {
            (await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None)).Should().Be(1);
            _time.Advance(TimeSpan.FromMinutes(15));
        }

        Row(ev).Status.Should().Be(OutboxEventStatus.Failed);
        (await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None)).Should().Be(0);
        _dispatchLogger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "DispatchGaveUp");
        _failureRecorder.Received(1).Record(Arg.Is<OperationalFailureReport>(r =>
            r != null
            && r.Engine == OperationalFailureEngine.BackgroundJob
            && r.Kind == OperationalFailureKind.JobFailedAfterRetries
            && r.CorrelationId == "corr-1"));
    }

    [Fact]
    public async Task UnknownType_IsRejected_AndLogged()
    {
        var ev = Enqueue(type: "document.exploded");

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        Row(ev).Status.Should().Be(OutboxEventStatus.Completed);
        Row(ev).Outcome.Should().Be(OutboxEventOutcome.Rejected);
        _processorLogger.Collector.GetSnapshot().Should().ContainSingle(r => r.Id.Name == "UnknownType" && r.Level == LogLevel.Error);
    }

    [Fact]
    public async Task SupportMailbox_MaterializesAnAddressOnlyNotification_OncePerEventKey()
    {
        var ev = Enqueue(new NotificationRecipient.SupportMailbox(), NotificationTypeKeys.AccountSupportRequestSubmitted, eventKey: "support:1", relatedItemType: "support-request");
        var duplicate = Enqueue(new NotificationRecipient.SupportMailbox(), NotificationTypeKeys.AccountSupportRequestSubmitted, eventKey: "support:1", relatedItemType: "support-request");

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        Row(ev).Outcome.Should().Be(OutboxEventOutcome.Materialized);
        Row(duplicate).Outcome.Should().Be(OutboxEventOutcome.Duplicate);
        var notification = _db.Committed.Should().ContainSingle().Subject;
        notification.RecipientUserId.Should().BeNull();
        notification.ShowInCenter.Should().BeFalse();
        notification.Deliveries.Should().ContainSingle(d => d.Channel == NotificationChannel.Email && d.RecipientKind == RecipientKind.SupportMailbox);
    }

    // ---- address lookup (specs/067 US9-B, FR-009e): password reset and confirmation resend ----

    private static string Sha256Hex(string value) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    [Fact]
    public async Task AddressLookup_ForAConfirmationResend_GoesToTheAccountsOwnAddress_EvenThoughItIsUnconfirmed()
    {
        _directory.Accounts[UserId] = _directory.Accounts[UserId] with { EmailConfirmed = false };
        var ev = Enqueue(new NotificationRecipient.AddressLookup("  Layla@Example.com "), NotificationTypeKeys.AccountEmailConfirmationRequested, eventKey: null, relatedItemType: null);

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        Row(ev).Outcome.Should().Be(OutboxEventOutcome.Materialized);
        var notification = _db.Committed.Should().ContainSingle().Subject;
        notification.RecipientUserId.Should().Be(UserId);
        notification.ShowInCenter.Should().BeFalse("account emails never appear in the notification center (FR-009c)");
        var email = notification.Deliveries.Should().ContainSingle().Subject;
        email.Channel.Should().Be(NotificationChannel.Email);
        email.Status.Should().Be(DeliveryStatus.Pending);
        email.RecipientKind.Should().Be(RecipientKind.Address, "the address being confirmed is routable although it is unverified");
        email.RecipientAddress.Should().Be("layla@example.com");
    }

    [Fact]
    public async Task AddressLookup_ForAPasswordReset_GoesToTheUsersVerifiedAddress_WithoutStoringIt()
    {
        var ev = Enqueue(new NotificationRecipient.AddressLookup("layla@example.com"), NotificationTypeKeys.AccountPasswordResetRequested, eventKey: null, relatedItemType: null);

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        Row(ev).Outcome.Should().Be(OutboxEventOutcome.Materialized);
        var email = _db.Committed.Should().ContainSingle().Subject.Deliveries.Should().ContainSingle().Subject;
        email.Status.Should().Be(DeliveryStatus.Pending);
        email.RecipientKind.Should().Be(RecipientKind.User);
        email.RecipientAddress.Should().BeNull();
        email.ExpiresAtUtc.Should().Be(_time.GetUtcNow().UtcDateTime.AddMinutes(60), "a reset request stays sendable for its 60 minutes");
    }

    [Fact]
    public async Task AddressLookup_ForAPasswordResetOfAnUnconfirmedAccount_SkipsTheEmail()
    {
        _directory.Accounts[UserId] = _directory.Accounts[UserId] with { EmailConfirmed = false };
        Enqueue(new NotificationRecipient.AddressLookup("layla@example.com"), NotificationTypeKeys.AccountPasswordResetRequested, eventKey: null, relatedItemType: null);

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        _db.Committed.Single().Deliveries.Should().ContainSingle(d => d.Status == DeliveryStatus.Skipped && d.SkipReason == DeliverySkipReason.NoVerifiedAddress);
    }

    [Theory]
    [InlineData("nobody@example.invalid")]
    [InlineData("  NoBody@Example.INVALID ")]
    public async Task AddressLookup_ForAnUnknownAddress_CompletesAsNoRecipient_AndLogsOnlyAHashOfIt(string address)
    {
        var ev = Enqueue(new NotificationRecipient.AddressLookup(address), NotificationTypeKeys.AccountPasswordResetRequested, eventKey: null, relatedItemType: null);

        var claimed = await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        claimed.Should().Be(1);
        Row(ev).Status.Should().Be(OutboxEventStatus.Completed);
        Row(ev).Outcome.Should().Be(OutboxEventOutcome.NoRecipient);
        _db.Committed.Should().BeEmpty("an unknown address creates no notification and no delivery");

        var logged = string.Join('\n', _processorLogger.Collector.GetSnapshot().Select(r => r.Message));
        logged.Should().Contain(Sha256Hex("nobody@example.invalid"));
        logged.Should().NotContainEquivalentOf("nobody").And.NotContainEquivalentOf("example.invalid");
    }

    [Fact]
    public async Task AddressLookup_ForADeletedAccount_IsNoRecipient_LikeAnUnknownAddress()
    {
        _directory.Accounts[UserId] = _directory.Accounts[UserId] with { IsActive = false };
        var ev = Enqueue(new NotificationRecipient.AddressLookup("layla@example.com"), NotificationTypeKeys.AccountPasswordResetRequested, eventKey: null, relatedItemType: null);

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        Row(ev).Outcome.Should().Be(OutboxEventOutcome.NoRecipient);
        _db.Committed.Should().BeEmpty();
    }

    [Fact]
    public async Task AddressLookup_KnownAndUnknownAddresses_BothCompleteTheSameWay_SoNothingTimingOrStatusDiffers()
    {
        var known = Enqueue(new NotificationRecipient.AddressLookup("layla@example.com"), NotificationTypeKeys.AccountPasswordResetRequested, eventKey: null, relatedItemType: null);
        var unknown = Enqueue(new NotificationRecipient.AddressLookup("nobody@example.invalid"), NotificationTypeKeys.AccountPasswordResetRequested, eventKey: null, relatedItemType: null);

        await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None);

        Row(known).Status.Should().Be(Row(unknown).Status).And.Be(OutboxEventStatus.Completed);
        Row(known).Attempts.Should().Be(Row(unknown).Attempts);
    }

    [Fact]
    public async Task EventLeasedToAnotherWorker_IsLeftAlone()
    {
        var ev = Enqueue();
        Row(ev).Claim("worker-2", _time.GetUtcNow().UtcDateTime.AddMinutes(2), _time.GetUtcNow().UtcDateTime);

        (await Dispatcher.DispatchBatchAsync(WorkerId, CancellationToken.None)).Should().Be(0);

        Row(ev).LeaseOwner.Should().Be("worker-2");
        _db.Committed.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(4, 80)]
    [InlineData(8, 900)]
    [InlineData(10, 900)]
    public void Backoff_DoublesFromTenSeconds_CappedAtFifteenMinutes(int attempts, int expectedSeconds)
    {
        OutboxDispatchService.BackoffFor(attempts).Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    /// <summary>What is persisted. The claim writes here directly, as the conditional update does (R4).</summary>
    private sealed class FakeDatabase(TimeProvider time)
    {
        public TimeProvider Time { get; } = time;

        public List<NotificationOutboxEvent> OutboxRows { get; } = [];

        public List<Notification> Committed { get; } = [];

        public bool ConflictOnSave { get; set; }
    }

    /// <summary>
    /// One scope's unit of work, modelled on EF: reads hand out tracked copies, a save writes them
    /// back, and a failed or conflicting scope leaves the database untouched.
    /// </summary>
    private sealed class FakeUnitOfWork(FakeDatabase db) : INotificationOutboxStore, INotificationRepository, IUnitOfWork
    {
        private static readonly MethodInfo CloneMethod =
            typeof(object).GetMethod(nameof(MemberwiseClone), BindingFlags.Instance | BindingFlags.NonPublic)!;

        private readonly List<NotificationOutboxEvent> _trackedEvents = [];
        private readonly List<Notification> _added = [];

        private DateTime Now => db.Time.GetUtcNow().UtcDateTime;

        public void Add(NotificationOutboxEvent outboxEvent) => _trackedEvents.Add(outboxEvent);

        public Task<IReadOnlyList<Guid>> ClaimBatchAsync(string workerId, DateTime leaseExpiresAtUtc, int batchSize, CancellationToken cancellationToken)
        {
            var claimable = db.OutboxRows
                .Where(e => (e.Status == OutboxEventStatus.Pending && e.NextAttemptAtUtc <= Now)
                    || (e.Status == OutboxEventStatus.Processing && e.LeaseExpiresAtUtc <= Now))
                .Take(batchSize)
                .ToList();
            claimable.ForEach(e => e.Claim(workerId, leaseExpiresAtUtc, Now));
            return Task.FromResult<IReadOnlyList<Guid>>([.. claimable.Select(e => e.Id)]);
        }

        public Task<NotificationOutboxEvent?> GetClaimedAsync(Guid id, string workerId, CancellationToken cancellationToken)
        {
            var row = db.OutboxRows.SingleOrDefault(e =>
                e.Id == id && e.Status == OutboxEventStatus.Processing && e.LeaseOwner == workerId && e.LeaseExpiresAtUtc > Now);
            if (row is null)
            {
                return Task.FromResult<NotificationOutboxEvent?>(null);
            }

            var tracked = (NotificationOutboxEvent)CloneMethod.Invoke(row, null)!;
            _trackedEvents.Add(tracked);
            return Task.FromResult<NotificationOutboxEvent?>(tracked);
        }

        public Task<int> ReleaseClaimsAsync(string workerId, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<NotificationOutboxEvent?> FindAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(db.OutboxRows.SingleOrDefault(e => e.Id == id));

        public Task<int> SweepExpiredLeasesAsync(DateTime now, CancellationToken cancellationToken) => Task.FromResult(0);

        // The delivery queue belongs to DeliveryProcessingServiceTests; the dispatcher never touches it.
        public Task<IReadOnlyList<Guid>> ClaimDueDeliveriesAsync(
            string workerId, IReadOnlyCollection<NotificationChannel> channels, DateTime leaseExpiresAtUtc, int batchSize, DateTime now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Notification?> GetClaimedDeliveryAsync(Guid deliveryId, string workerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> ReleaseClaimsAsync(string workerId, IReadOnlyCollection<Guid> deliveryIds, DateTime now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<DeliveryBacklog> GetDueBacklogAsync(DateTime now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Add(Notification notification) => _added.Add(notification);

        public Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(db.Committed.SingleOrDefault(n => n.Id == id));

        public Task<IReadOnlySet<string>> GetRecipientsWithEventKeyAsync(string eventKey, IReadOnlyCollection<string> recipientUserIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(db.Committed
                .Where(n => n.EventKey == eventKey && n.RecipientUserId is not null && recipientUserIds.Contains(n.RecipientUserId))
                .Select(n => n.RecipientUserId!)
                .ToHashSet(StringComparer.Ordinal));

        public Task<bool> ExistsForAddressAsync(string eventKey, CancellationToken cancellationToken) =>
            Task.FromResult(db.Committed.Any(n => n.EventKey == eventKey && n.RecipientUserId is null));

        public Task<IReadOnlyDictionary<string, int>> CountUnreadAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(userIds.ToDictionary(
                id => id,
                id => db.Committed.Count(n => n.RecipientUserId == id && n.ShowInCenter && !n.IsRead)));

        // The three members below are exercised by NotificationCenterHandlerTests and
        // NotificationCenterQueryTests, not this class's own tests — they only need to compile
        // and to mirror NotificationRepository's real filters closely enough not to lie.
        public Task<int> CountUnreadAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(db.Committed.Count(n => n.RecipientUserId == userId && n.ShowInCenter && !n.IsRead));

        public Task<(IReadOnlyList<Notification> Items, string? NextCursor)> ListAsync(
            string userId,
            IReadOnlyCollection<NotificationCategory>? categories,
            NotificationReadState state,
            string? cursor,
            int limit,
            CancellationToken cancellationToken)
        {
            var query = db.Committed.Where(n => n.RecipientUserId == userId && n.ShowInCenter);
            if (categories is { Count: > 0 })
            {
                query = query.Where(n => categories.Contains(n.Category));
            }

            query = state switch
            {
                NotificationReadState.Unread => query.Where(n => n.ReadAtUtc == null),
                NotificationReadState.Read => query.Where(n => n.ReadAtUtc != null),
                _ => query,
            };

            var items = query.OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id).Take(limit).ToList();
            return Task.FromResult<(IReadOnlyList<Notification>, string?)>((items, null));
        }

        public Task<int> MarkAllReadAsync(string userId, NotificationCategory? category, DateTime now, CancellationToken cancellationToken)
        {
            var matches = db.Committed.Where(n =>
                n.RecipientUserId == userId &&
                n.ShowInCenter &&
                n.ReadAtUtc == null &&
                (n.Status == NotificationStatus.Delivered || n.Status == NotificationStatus.Sent) &&
                (category is null || n.Category == category));

            var updated = 0;
            foreach (var notification in matches)
            {
                notification.MarkRead(now);
                updated++;
            }

            return Task.FromResult(updated);
        }

        public Task<int> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken)
        {
            var removed = db.Committed.RemoveAll(n => n.RecipientUserId == userId);
            return Task.FromResult(removed);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            foreach (var tracked in _trackedEvents)
            {
                db.OutboxRows[db.OutboxRows.FindIndex(e => e.Id == tracked.Id)] = tracked;
            }

            db.Committed.AddRange(_added);
            var count = _trackedEvents.Count + _added.Count;
            _trackedEvents.Clear();
            _added.Clear();
            return Task.FromResult(count);
        }

        public async Task<bool> TrySaveChangesAsync(string uniqueIndexNameOnConflict, CancellationToken cancellationToken = default)
        {
            if (db.ConflictOnSave)
            {
                _trackedEvents.Clear();
                _added.Clear();
                return false;
            }

            await SaveChangesAsync(cancellationToken);
            return true;
        }
    }

    private sealed class FakeRecipientDirectory : INotificationRecipientDirectory
    {
        public Dictionary<string, NotificationRecipientInfo> Accounts { get; } = [];

        public Task<NotificationRecipientInfo?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(Accounts.Values.FirstOrDefault(a => string.Equals(a.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyDictionary<string, NotificationRecipientInfo>> GetAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, NotificationRecipientInfo>>(
                Accounts.Where(a => userIds.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value));
    }

    private sealed class FakeRenderer : INotificationTemplateRenderer
    {
        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public IReadOnlyDictionary<string, string?> LastVariables { get; private set; } = new Dictionary<string, string?>();

        public Task<RenderedInApp> RenderInAppAsync(
            NotificationTypeDefinition definition, string language, IReadOnlyDictionary<string, string?> variables, CancellationToken cancellationToken)
        {
            Calls++;
            LastVariables = variables;
            if (Fail)
            {
                throw new NotificationRenderException("The in-app template has an unclosed placeholder.");
            }

            return Task.FromResult(new RenderedInApp($"{variables["documentName"]} failed", "Processing failed.", "Open document", Guid.CreateVersion7(), language));
        }

        public Task<RenderedEmail> RenderEmailAsync(
            NotificationTypeDefinition definition, string language, IReadOnlyDictionary<string, string?> variables, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The dispatcher renders in-app only; email renders at send time.");
    }

    private sealed record Push(string UserId, NotificationListItemDto Item, int UnreadCount, bool WasCommitted);

    private sealed class FakeRealtime(FakeDatabase db) : INotificationRealtimePublisher
    {
        public bool Fail { get; set; }

        public List<Push> Pushes { get; } = [];

        public Task NotificationCreatedAsync(string userId, NotificationListItemDto item, int unreadCount, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("The hub connection is down.");
            }

            Pushes.Add(new Push(userId, item, unreadCount, db.Committed.Any(n => n.Id == item.Id)));
            return Task.CompletedTask;
        }

        public Task NotificationUpdatedAsync(string userId, Guid notificationId, NotificationChange change, int unreadCount, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UnreadCountChangedAsync(string userId, int unreadCount, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
