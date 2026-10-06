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
/// T106 — the delivery relay over in-memory fakes (specs/067 US3, research R4 to R6): the retry schedule
/// for normal and Critical priority, the dead letter after <c>MaxAttempts</c>, an immediate permanent
/// failure, a deleted recipient, an expired notification, the language resolved at send time, the
/// aggregate status and the metrics. Each delivery runs in its own DI scope, as in production.
/// </summary>
public sealed class DeliveryProcessingServiceTests : IDisposable
{
    private const string WorkerId = "worker-1";
    private const string UserId = "user-1";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeDeliveryStore _store;
    private readonly FakeChannelSender _sender = new();
    private readonly Dictionary<string, NotificationRecipientInfo> _accounts = [];
    private readonly IEffectiveLanguageResolver _languages = Substitute.For<IEffectiveLanguageResolver>();
    private readonly INotificationLinkBuilder _links = Substitute.For<INotificationLinkBuilder>();
    private readonly IAccountLinkIssuer _linkIssuer = Substitute.For<IAccountLinkIssuer>();
    private readonly ISupportMailboxResolver _supportMailbox = Substitute.For<ISupportMailboxResolver>();
    private readonly INotificationMetrics _metrics = Substitute.For<INotificationMetrics>();
    private readonly INotificationAuditWriter _audit = Substitute.For<INotificationAuditWriter>();
    private readonly IOperationalFailureRecorder _failureRecorder = Substitute.For<IOperationalFailureRecorder>();
    private readonly FakeLogger<DeliveryProcessor> _processorLogger = new();
    private readonly FakeLogger<DeliveryProcessingService> _serviceLogger = new();
    private readonly ServiceProvider _services;

    public DeliveryProcessingServiceTests()
    {
        _store = new FakeDeliveryStore(_time);
        _accounts[UserId] = new NotificationRecipientInfo(UserId, "Layla", "layla@example.com", EmailConfirmed: true, IsActive: true);
        _languages.ResolveAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns("en");
        _links.BuildAbsolute(Arg.Any<NotificationTypeDefinition>(), Arg.Any<RelatedItem?>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, string?>?>())
            .Returns("https://app.example.test/workflows/w1/executions/e1");
        _supportMailbox.GetAddress().Returns("support@example.test");

        var channels = Substitute.For<INotificationChannelRegistry>();
        channels.AvailableChannels.Returns(new HashSet<NotificationChannel> { NotificationChannel.InApp, NotificationChannel.Email });
        var directory = Substitute.For<INotificationRecipientDirectory>();
        directory.GetAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyDictionary<string, NotificationRecipientInfo>>(
                _accounts.Where(a => call.Arg<IReadOnlyCollection<string>>()!.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value)));

        _services = new ServiceCollection()
            .AddSingleton<TimeProvider>(_time)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(new NotificationsOptions()))
            .AddSingleton(_store)
            .AddSingleton<INotificationRepository>(_store)
            .AddSingleton<INotificationOutboxStore>(_store)
            .AddSingleton<AskLucy.Application.Abstractions.IUnitOfWork>(_store)
            .AddSingleton(channels)
            .AddSingleton(directory)
            .AddSingleton(_languages)
            .AddSingleton(_links)
            .AddSingleton(_linkIssuer)
            .AddSingleton(_supportMailbox)
            .AddSingleton<INotificationChannelSender>(_sender)
            .AddSingleton(_metrics)
            .AddSingleton(_audit)
            .AddSingleton(_failureRecorder)
            .AddSingleton<ILogger<DeliveryProcessor>>(_processorLogger)
            .AddSingleton<ILogger<DeliveryProcessingService>>(_serviceLogger)
            .AddScoped<DeliveryProcessor>()
            .AddSingleton<DeliveryProcessingService>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public void Dispose() => _services.Dispose();

    private DeliveryProcessingService Service => _services.GetRequiredService<DeliveryProcessingService>();

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>A workflow-failure notification with a pending email delivery, plus its event.</summary>
    private Notification Enqueue(
        NotificationPriority priority = NotificationPriority.High,
        string type = NotificationTypeKeys.WorkflowExecutionFailed,
        RecipientKind kind = RecipientKind.User,
        string? address = null,
        string? userId = UserId,
        DateTime? deliveryExpiresAtUtc = null,
        bool alsoInApp = false,
        string variablesJson = """{"workflowName":"Nightly build","failureSummary":"Step 3 timed out"}""",
        string? explicitLanguage = null)
    {
        var definition = NotificationTypeCatalog.Get(type);
        var ev = NotificationOutboxEvent.Create(
            type, """{"kind":"User","userId":"user-1"}""", variablesJson, "corr-1", Now, "workflow-execution:e1:failed",
            "workflow-execution", "e1", "w1", explicitLanguage);
        _store.Events[ev.Id] = ev;

        var notification = Notification.Create(
            userId, definition, priority, alsoInApp ? "Workflow failed" : string.Empty, alsoInApp ? "It failed." : string.Empty, "en", "corr-1", Now,
            showInCenter: alsoInApp, relatedItemType: "workflow-execution", relatedItemId: "e1", sourceEventId: ev.Id);
        if (alsoInApp)
        {
            notification.AddDelivery(NotificationDelivery.CreateDelivered(NotificationChannel.InApp, priority, "en", null, "corr-1", Now));
        }

        notification.AddDelivery(NotificationDelivery.CreatePending(
            NotificationChannel.Email, priority, kind, address, maxAttempts: 5, deliveryExpiresAtUtc, "corr-1", Now));
        _store.Notifications.Add(notification);
        return notification;
    }

    private Task<DeliveryBatchResult> RunBatchAsync() => RunBatchWithTokenAsync(TestContext.Current.CancellationToken);

    private Task<DeliveryBatchResult> RunBatchWithTokenAsync(CancellationToken cancellationToken) =>
        Service.ProcessBatchAsync(WorkerId, cancellationToken);

    // ---- the happy path ----

    [Fact]
    public async Task Sent_ApprovalRequestEmail_WritesOneDeliveredAuditRow_WithTheNotificationsCorrelationId()
    {
        var notification = Enqueue(type: NotificationTypeKeys.WorkflowApprovalRequested);
        _sender.Script(ChannelSendResult.Sent("SMTP accepted", Guid.CreateVersion7(), "en"));

        await RunBatchAsync();

        _audit.Received(1).Write(
            NotificationAuditAction.ApprovalNotificationDelivered, "Notification", notification.Id.ToString(),
            NotificationAuditOutcome.Succeeded, Arg.Any<object?>(), "corr-1");
    }

    [Fact]
    public async Task Sent_OrdinaryEmail_WritesNoApprovalAuditRow()
    {
        Enqueue();
        _sender.Script(ChannelSendResult.Sent("SMTP accepted", Guid.CreateVersion7(), "en"));

        await RunBatchAsync();

        _audit.DidNotReceiveWithAnyArgs().Write(default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Failed_ApprovalRequestEmail_WritesNoDeliveredAuditRow()
    {
        Enqueue(type: NotificationTypeKeys.WorkflowApprovalRequested);
        _sender.Script(ChannelSendResult.Permanent(DeliveryFailureKind.Permanent, "The mail server permanently rejected the message (550).", "SMTP 550 5.1.1"));

        await RunBatchAsync();

        _audit.DidNotReceiveWithAnyArgs().Write(default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Pending_DeliveryIsClaimedSentAndRecorded_WithTheTemplateVersionAndLanguageThatWereRendered()
    {
        Enqueue();
        var version = Guid.CreateVersion7();
        _sender.Script(ChannelSendResult.Sent("SMTP accepted", version, "en"));

        var batch = await RunBatchAsync();

        batch.Should().Be(new DeliveryBatchResult(Claimed: 1, Processed: 1, DeferredFor: null));
        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.Sent);
        delivery.SentAtUtc.Should().Be(Now);
        delivery.TemplateVersionId.Should().Be(version);
        delivery.Language.Should().Be("en");
        delivery.ProviderResponse.Should().Be("SMTP accepted");
        delivery.AttemptCount.Should().Be(1);
        delivery.LeaseOwner.Should().BeNull();
        _store.Saves.Should().Be(1);
        _metrics.Received(1).DeliverySent(NotificationChannel.Email, Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task Claim_AsksOnlyForChannelsWithASender_NeverInApp()
    {
        await RunBatchAsync();

        _store.ClaimedChannels.Should().ContainSingle().Which.Should().BeEquivalentTo([NotificationChannel.Email]);
    }

    [Fact]
    public async Task Send_ReceivesTheRecipientAddress_TheDeclaredVariables_TheStandardOnes_AndTheAbsoluteLink()
    {
        Enqueue();

        await RunBatchAsync();

        var context = _sender.Contexts.Should().ContainSingle().Subject;
        context.RecipientAddress.Should().Be("layla@example.com");
        context.Definition.Key.Should().Be(NotificationTypeKeys.WorkflowExecutionFailed);
        context.Variables["workflowName"].Should().Be("Nightly build");
        context.Variables["failureSummary"].Should().Be("Step 3 timed out");
        context.Variables["recipientDisplayName"].Should().Be("Layla");
        context.Variables["actionUrl"].Should().Be("https://app.example.test/workflows/w1/executions/e1");
        context.Variables["occurredAt"].Should().Be("2026-10-06 09:00 UTC");
        context.IsMandatory.Should().BeFalse();
        context.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task Send_ForAMandatoryType_DrawsFromTheReservedLane()
    {
        Enqueue(NotificationPriority.Critical, NotificationTypeKeys.SecurityTwoFactorEnabled);

        await RunBatchAsync();

        _sender.Contexts.Should().ContainSingle().Which.IsMandatory.Should().BeTrue();
    }

    // ---- language at send time ----

    [Fact]
    public async Task Language_IsResolvedAtSendTime_FromTheRecipientAndTheEventsExplicitLanguage()
    {
        Enqueue(explicitLanguage: "ar");
        _languages.ResolveAsync(UserId, "ar", Arg.Any<CancellationToken>()).Returns("ar");

        await RunBatchAsync();

        _sender.Contexts.Should().ContainSingle().Which.Language.Should().Be("ar");
    }

    [Fact]
    public async Task Language_ChangedBetweenQueueingAndSending_UsesTheLanguageAtSendTime()
    {
        Enqueue();
        _languages.ResolveAsync(UserId, null, Arg.Any<CancellationToken>()).Returns("en");
        _time.Advance(TimeSpan.FromMinutes(5));
        _languages.ResolveAsync(UserId, null, Arg.Any<CancellationToken>()).Returns("ar");

        await RunBatchAsync();

        _sender.Contexts.Should().ContainSingle().Which.Language.Should().Be("ar");
    }

    // ---- retry schedule ----

    [Theory]
    [InlineData(NotificationPriority.Normal, 1, 60)]
    [InlineData(NotificationPriority.Normal, 2, 240)]
    [InlineData(NotificationPriority.Normal, 3, 600)]
    [InlineData(NotificationPriority.Normal, 4, 1200)]
    [InlineData(NotificationPriority.High, 1, 60)]
    [InlineData(NotificationPriority.Critical, 1, 30)]
    [InlineData(NotificationPriority.Critical, 2, 60)]
    [InlineData(NotificationPriority.Critical, 3, 120)]
    [InlineData(NotificationPriority.Critical, 4, 300)]
    public async Task TransientFailure_RetriesOnTheNormalOrCriticalSchedule(NotificationPriority priority, int failedAttempts, int expectedDelaySeconds)
    {
        Enqueue(priority);
        for (var attempt = 1; attempt <= failedAttempts; attempt++)
        {
            _sender.Script(ChannelSendResult.Transient("The mail server temporarily refused the message (451).", "SMTP 451"));
            await RunBatchAsync();

            var delivery = _store.Delivery();
            delivery.AttemptCount.Should().Be(attempt);
            if (attempt < failedAttempts)
            {
                _time.SetUtcNow(new DateTimeOffset(delivery.NextAttemptAtUtc!.Value, TimeSpan.Zero));
            }
        }

        var last = _store.Delivery();
        last.Status.Should().Be(DeliveryStatus.Retrying);
        last.FailureKind.Should().Be(DeliveryFailureKind.Transient);
        last.FailureReason.Should().Contain("451");
        last.ProviderResponse.Should().Be("SMTP 451");
        last.NextAttemptAtUtc.Should().Be(Now.AddSeconds(expectedDelaySeconds));
        _metrics.Received(failedAttempts).DeliveryRetried(NotificationChannel.Email);
    }

    [Fact]
    public async Task TransientFailure_OnTheLastAttempt_IsDeadLettered_AndOnTheOperationalFailureTrail()
    {
        Enqueue();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            _sender.Script(ChannelSendResult.Transient("The connection to the mail server failed.", "SMTP connection error"));
            await RunBatchAsync();
            if (attempt < 5)
            {
                _time.SetUtcNow(new DateTimeOffset(_store.Delivery().NextAttemptAtUtc!.Value, TimeSpan.Zero));
            }
        }

        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.DeadLettered);
        delivery.FailureKind.Should().Be(DeliveryFailureKind.RetryLimitReached);
        delivery.AttemptCount.Should().Be(5);
        delivery.NextAttemptAtUtc.Should().BeNull();
        _store.Notifications.Single().Status.Should().Be(NotificationStatus.Failed);
        _metrics.Received(1).DeliveryDeadLettered(NotificationChannel.Email);
        _metrics.Received(4).DeliveryRetried(NotificationChannel.Email);
        _failureRecorder.Received(1).Record(Arg.Is<OperationalFailureReport>(r =>
            r != null && r.Kind == OperationalFailureKind.JobFailedAfterRetries && r.CorrelationId == "corr-1"));
    }

    [Fact]
    public async Task DeadLetteredDelivery_IsNeverClaimedAgain()
    {
        Enqueue();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            _sender.Script(ChannelSendResult.Transient("temporary", null));
            await RunBatchAsync();
            _time.Advance(TimeSpan.FromHours(1));
        }

        var batch = await RunBatchAsync();

        batch.Claimed.Should().Be(0);
        _sender.Contexts.Should().HaveCount(5);
    }

    [Fact]
    public async Task RetryNotYetDue_IsNotClaimed()
    {
        Enqueue();
        _sender.Script(ChannelSendResult.Transient("temporary", null));
        await RunBatchAsync();

        var batch = await RunBatchAsync();

        batch.Claimed.Should().Be(0);
    }

    [Fact]
    public async Task AuthenticationFailure_IsRetried_AndPutsTheChannelOnTheOperationalFailureTrail()
    {
        Enqueue();
        _sender.Script(ChannelSendResult.Transient("The mail server rejected the sign-in; the SMTP credentials need attention.", "SMTP authentication failed", requiresAttention: true));

        await RunBatchAsync();

        _store.Delivery().Status.Should().Be(DeliveryStatus.Retrying);
        _failureRecorder.Received(1).Record(Arg.Is<OperationalFailureReport>(r => r != null && r.Kind == OperationalFailureKind.CredentialRejected));
    }

    // ---- permanent failures ----

    [Fact]
    public async Task PermanentFailure_FailsImmediately_WithoutARetry()
    {
        Enqueue();
        _sender.Script(ChannelSendResult.Permanent(DeliveryFailureKind.Permanent, "The mail server permanently rejected the message (550).", "SMTP 550 5.1.1"));

        await RunBatchAsync();

        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.Failed);
        delivery.FailureKind.Should().Be(DeliveryFailureKind.Permanent);
        delivery.FailureReason.Should().Contain("550");
        delivery.NextAttemptAtUtc.Should().BeNull();
        _store.Notifications.Single().Status.Should().Be(NotificationStatus.Failed);
        _metrics.Received(1).DeliveryFailed(NotificationChannel.Email, DeliveryFailureKind.Permanent);
        _metrics.Received(1).ProviderError(NotificationChannel.Email, DeliveryFailureKind.Permanent);
        _metrics.DidNotReceive().DeliveryRetried(Arg.Any<NotificationChannel>());
    }

    [Fact]
    public async Task RenderError_FailsImmediately_AndIsNotCountedAsAProviderError()
    {
        Enqueue();
        _sender.Script(ChannelSendResult.Permanent(DeliveryFailureKind.RenderError, "The email template could not be rendered."));

        await RunBatchAsync();

        _store.Delivery().FailureKind.Should().Be(DeliveryFailureKind.RenderError);
        _metrics.Received(1).DeliveryFailed(NotificationChannel.Email, DeliveryFailureKind.RenderError);
        _metrics.DidNotReceive().ProviderError(Arg.Any<NotificationChannel>(), Arg.Any<DeliveryFailureKind>());
    }

    [Fact]
    public async Task ASenderThatThrows_IsTreatedAsATransientFailure_AndLogged()
    {
        Enqueue();
        _sender.Script(new InvalidOperationException("bug in the sender"));

        await RunBatchAsync();

        _store.Delivery().Status.Should().Be(DeliveryStatus.Retrying);
        _processorLogger.Collector.GetSnapshot().Should().Contain(r => r.Level == LogLevel.Error && r.Exception != null);
    }

    // ---- re-validation before sending ----

    [Fact]
    public async Task DeletedRecipient_CancelsTheDelivery_WithoutSending()
    {
        Enqueue();
        _accounts.Remove(UserId);

        await RunBatchAsync();

        _sender.Contexts.Should().BeEmpty();
        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.Cancelled);
        delivery.FailureKind.Should().Be(DeliveryFailureKind.RecipientUnavailable);
        _store.Notifications.Single().Status.Should().Be(NotificationStatus.Cancelled);
    }

    [Fact]
    public async Task DeactivatedRecipient_CancelsTheDelivery_WithoutSending()
    {
        Enqueue();
        _accounts[UserId] = _accounts[UserId] with { IsActive = false };

        await RunBatchAsync();

        _sender.Contexts.Should().BeEmpty();
        _store.Delivery().Status.Should().Be(DeliveryStatus.Cancelled);
    }

    [Fact]
    public async Task RecipientWhoNoLongerHasAVerifiedAddress_CancelsTheDelivery()
    {
        Enqueue();
        _accounts[UserId] = _accounts[UserId] with { EmailConfirmed = false };

        await RunBatchAsync();

        _sender.Contexts.Should().BeEmpty();
        _store.Delivery().Status.Should().Be(DeliveryStatus.Cancelled);
        _store.Delivery().FailureReason.Should().Contain("verified");
    }

    [Fact]
    public async Task ExpiredDelivery_IsExpired_WithoutSending()
    {
        Enqueue(deliveryExpiresAtUtc: Now.AddMinutes(10));
        _time.Advance(TimeSpan.FromMinutes(11));

        await RunBatchAsync();

        _sender.Contexts.Should().BeEmpty();
        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.Expired);
        delivery.FailureKind.Should().Be(DeliveryFailureKind.RequestExpired);
        _store.Notifications.Single().Status.Should().Be(NotificationStatus.Expired);
    }

    [Fact]
    public async Task ExpiredNotification_ExpiresItsClaimedDelivery_WithoutSending()
    {
        var notification = Enqueue();
        typeof(Notification).GetProperty(nameof(Notification.ExpiresAtUtc))!.SetValue(notification, Now.AddMinutes(-1));

        await RunBatchAsync();

        _sender.Contexts.Should().BeEmpty();
        _store.Delivery().Status.Should().Be(DeliveryStatus.Expired);
    }

    [Fact]
    public async Task NotificationDeletedByItsOwner_CancelsTheEmail()
    {
        var notification = Enqueue(alsoInApp: true);
        notification.DeleteByOwner(UserId, Now);

        await RunBatchAsync();

        _sender.Contexts.Should().BeEmpty();
        _store.Delivery().Status.Should().Be(DeliveryStatus.Cancelled);
    }

    // ---- recipient kinds ----

    [Fact]
    public async Task AddressDelivery_GoesToTheStoredAddress_AndGreetsTheAddressWhenTheAccountHasNoName()
    {
        _accounts[UserId] = _accounts[UserId] with { DisplayName = null };
        Enqueue(type: NotificationTypeKeys.AccountEmailChangeRequested, kind: RecipientKind.Address, address: "new@example.com");
        _linkIssuer.IssueAsync(SensitiveLinkKind.EmailChange, UserId, "new@example.com", false, Arg.Any<CancellationToken>())
            .Returns(new Uri("https://app.example.test/confirm-email-change?token=abc"));

        await RunBatchAsync();

        var context = _sender.Contexts.Should().ContainSingle().Subject;
        context.RecipientAddress.Should().Be("new@example.com");
        context.Variables["recipientDisplayName"].Should().Be("new@example.com");
    }

    [Fact]
    public async Task SupportMailboxDelivery_GoesToTheConfiguredAddress_ThatIsNeverStoredOnTheDelivery()
    {
        Enqueue(type: NotificationTypeKeys.AccountSupportRequestSubmitted, kind: RecipientKind.SupportMailbox, userId: null);

        await RunBatchAsync();

        _sender.Contexts.Should().ContainSingle().Which.RecipientAddress.Should().Be("support@example.test");
        _store.Delivery().RecipientAddress.Should().BeNull();
    }

    [Fact]
    public async Task SupportMailboxDelivery_WithNoConfiguredMailbox_FailsVisibly()
    {
        Enqueue(type: NotificationTypeKeys.AccountSupportRequestSubmitted, kind: RecipientKind.SupportMailbox, userId: null);
        _supportMailbox.GetAddress().Returns((string?)null);

        await RunBatchAsync();

        _sender.Contexts.Should().BeEmpty();
        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.Failed);
        delivery.FailureReason.Should().Contain("support mailbox");
    }

    // ---- one-time links (US9-B, R10) ----

    [Fact]
    public async Task AccountLink_IsMintedAtSendTime_PassedOnlyAsTheActionUrl_AndNeverPersisted()
    {
        const string secretLink = "https://app.example.test/reset-password?userId=user-1&token=S3CRET-TOKEN";
        Enqueue(NotificationPriority.Critical, NotificationTypeKeys.AccountPasswordResetRequested);
        _linkIssuer.IssueAsync(SensitiveLinkKind.PasswordReset, UserId, "layla@example.com", false, Arg.Any<CancellationToken>())
            .Returns(new Uri(secretLink));
        _sender.Script(ChannelSendResult.Sent("SMTP accepted", Guid.CreateVersion7(), "en"));

        await RunBatchAsync();

        _sender.Contexts.Should().ContainSingle().Which.Variables["actionUrl"].Should().Be(secretLink);
        var delivery = _store.Delivery();
        var stored = string.Join('|', delivery.ProviderResponse, delivery.FailureReason, delivery.RecipientAddress, delivery.CorrelationId);
        stored.Should().NotContain("S3CRET-TOKEN");
        _processorLogger.Collector.GetSnapshot().Select(r => r.Message).Should().NotContain(m => m.Contains("S3CRET-TOKEN"));
        _links.DidNotReceive().BuildAbsolute(Arg.Any<NotificationTypeDefinition>(), Arg.Any<RelatedItem?>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, string?>?>());
    }

    [Fact]
    public async Task AccountLink_OnARetry_IsFlaggedSoItIsNotThrottledAsANewRequest()
    {
        Enqueue(NotificationPriority.Critical, NotificationTypeKeys.AccountPasswordResetRequested);
        _linkIssuer.IssueAsync(Arg.Any<SensitiveLinkKind>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Uri("https://app.example.test/reset-password?token=t"));
        _sender.Script(ChannelSendResult.Transient("temporary", null));
        await RunBatchAsync();
        _time.SetUtcNow(new DateTimeOffset(_store.Delivery().NextAttemptAtUtc!.Value, TimeSpan.Zero));

        await RunBatchAsync();

        await _linkIssuer.Received(1).IssueAsync(SensitiveLinkKind.PasswordReset, UserId, "layla@example.com", false, Arg.Any<CancellationToken>());
        await _linkIssuer.Received(1).IssueAsync(SensitiveLinkKind.PasswordReset, UserId, "layla@example.com", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AccountLink_Refused_CancelsTheDelivery_WithoutSending()
    {
        Enqueue(NotificationPriority.Critical, NotificationTypeKeys.AccountPasswordResetRequested);
        _linkIssuer.IssueAsync(Arg.Any<SensitiveLinkKind>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns<Uri>(_ => throw new AccountLinkRefusedException("No link was issued."));

        await RunBatchAsync();

        _sender.Contexts.Should().BeEmpty();
        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.Cancelled);
        delivery.FailureReason.Should().Be("No link was issued.");
    }

    // ---- aggregate status and the in-app copy ----

    [Fact]
    public async Task Aggregate_StaysDelivered_WhenTheInAppCopyIsInTheCenter_EvenIfTheEmailFails()
    {
        Enqueue(alsoInApp: true);
        _sender.Script(ChannelSendResult.Permanent(DeliveryFailureKind.Permanent, "rejected", null));

        await RunBatchAsync();

        _store.Delivery().Status.Should().Be(DeliveryStatus.Failed);
        _store.Notifications.Single().Status.Should().Be(NotificationStatus.Delivered);
    }

    [Fact]
    public async Task Aggregate_IsSent_ForAnEmailOnlyNotificationOnceItIsSent()
    {
        Enqueue(NotificationPriority.Critical, NotificationTypeKeys.SecurityPasswordChanged);
        _sender.Script(ChannelSendResult.Sent("SMTP accepted", Guid.CreateVersion7(), "en"));

        await RunBatchAsync();

        _store.Notifications.Single().Status.Should().Be(NotificationStatus.Sent);
    }

    // ---- priority and batching ----

    [Fact]
    public async Task Batch_SendsCriticalBeforeNormal_EvenWhenTheNormalOnesAreOlder()
    {
        Enqueue(NotificationPriority.Normal);
        _time.Advance(TimeSpan.FromMinutes(1));
        Enqueue(NotificationPriority.Critical, NotificationTypeKeys.SecurityTwoFactorEnabled);

        await RunBatchAsync();

        _sender.Contexts.Select(c => c.Priority).Should().Equal(NotificationPriority.Critical, NotificationPriority.Normal);
    }

    // ---- send limiter deferral ----

    [Fact]
    public async Task Deferred_PutsTheClaimBackUnspent_LeavesTheRestUntouched_AndTellsTheWorkerHowLongToWait()
    {
        Enqueue();
        Enqueue();
        _sender.Script(ChannelSendResult.Deferred(TimeSpan.FromSeconds(20)));

        var batch = await RunBatchAsync();

        batch.Should().Be(new DeliveryBatchResult(Claimed: 1, Processed: 0, DeferredFor: TimeSpan.FromSeconds(20)));
        _sender.Contexts.Should().HaveCount(1, "once the channel is out of capacity the rest of the queue is not even claimed");
        _store.AllDeliveries.Should().OnlyContain(d => d.Status == DeliveryStatus.Pending && d.AttemptCount == 0 && d.LeaseOwner == null);
        _store.AllDeliveries.Should().ContainSingle(d => d.NextAttemptAtUtc == Now.AddSeconds(20));
        _metrics.DidNotReceive().DeliveryRetried(Arg.Any<NotificationChannel>());
    }

    [Fact]
    public async Task Claims_AreMadeOneDeliveryAtATime_SoALeaseCoversOnlyThatDeliverysOwnSend()
    {
        for (var i = 0; i < 5; i++)
        {
            Enqueue();
        }

        await RunBatchAsync();

        // Five deliveries, five claims, and a sixth that found nothing due: each delivery's lease started
        // just before its own send, never while it waited behind the others.
        _store.ClaimCalls.Should().Be(6);
        _store.ClaimBatchSizes.Should().OnlyContain(size => size == 1);
    }

    [Fact]
    public async Task Deferred_DoesNotCountAsAnAttempt_SoItNeverDeadLettersADeliveryTheLimiterHeldBack()
    {
        Enqueue();
        for (var i = 0; i < 10; i++)
        {
            _sender.Script(ChannelSendResult.Deferred(TimeSpan.FromSeconds(1)));
            await RunBatchAsync();
            _time.Advance(TimeSpan.FromSeconds(2));
        }

        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(0);
    }

    // ---- failures around the send ----

    [Fact]
    public async Task AFailureBeforeTheSend_CountsTheAttempt_AndRetriesOnSchedule_AndIsLogged()
    {
        Enqueue();
        _languages.ResolveAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("language lookup down"));

        await RunBatchAsync();

        _sender.Contexts.Should().BeEmpty();
        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.Retrying);
        delivery.AttemptCount.Should().Be(1, "a failure to prepare uses up an attempt, so a poison delivery can't be claimed for ever");
        delivery.NextAttemptAtUtc.Should().Be(Now.AddMinutes(1));
        delivery.FailureKind.Should().Be(DeliveryFailureKind.Transient);
        delivery.FailureReason.Should().Contain("prepared").And.NotContain("language lookup down", "the exception text stays in the log");
        _serviceLogger.Collector.GetSnapshot().Should().Contain(r => r.Level == LogLevel.Error && r.Exception != null);
        _store.ReleasedIds.Should().BeEmpty("the failure was recorded, so the claim is not handed back");
    }

    [Fact]
    public async Task AFailureBeforeTheSend_EveryTime_EndsAsADeadLetter_NotAnEndlessReclaim()
    {
        Enqueue();
        _languages.ResolveAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("language lookup down"));

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await RunBatchAsync();
            _time.Advance(TimeSpan.FromHours(1));
        }

        _store.Delivery().Status.Should().Be(DeliveryStatus.DeadLettered);
        (await RunBatchAsync()).Claimed.Should().Be(0);
        _failureRecorder.Received(1).Record(Arg.Is<OperationalFailureReport>(r => r != null && r.Kind == OperationalFailureKind.JobFailedAfterRetries));
    }

    [Fact]
    public async Task AFailureBeforeTheSend_ThatCannotBeRecordedEither_GivesTheClaimBack()
    {
        Enqueue();
        _languages.ResolveAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("language lookup down"));
        _store.SaveFailure = new InvalidOperationException("database unreachable");

        await RunBatchAsync();

        _store.ReleasedIds.Should().Contain(_store.Delivery().Id);
        _serviceLogger.Collector.GetSnapshot().Count(r => r.Level == LogLevel.Error).Should().BeGreaterThanOrEqualTo(2, "both failures are logged");
    }

    [Fact]
    public async Task AFailureToSaveAfterTheSend_IsLogged_AndTheDeliveryIsNotReleased_BecauseReleasingItWouldResendIt()
    {
        Enqueue();
        _sender.Script(ChannelSendResult.Sent("SMTP accepted", Guid.CreateVersion7(), "en"));
        _store.SaveFailure = new InvalidOperationException("concurrent update");

        var batch = await RunBatchAsync();

        // The email went out; recording it failed. The claim stays with its lease, and once that expires
        // the sweeper records the delivery as ambiguous (R5), visible to an administrator.
        batch.Processed.Should().Be(0);
        _store.ReleasedIds.Should().BeEmpty();
        _serviceLogger.Collector.GetSnapshot().Should().Contain(r =>
            r.Level == LogLevel.Error && r.Exception != null && r.Message.Contains("SendStarted True", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostShutdownMidSend_LeavesThatDeliveryForTheSweeper_AndGivesBackTheOnesNotYetStarted()
    {
        Enqueue(NotificationPriority.Critical, NotificationTypeKeys.SecurityTwoFactorEnabled);
        Enqueue(NotificationPriority.Normal);
        using var shutdown = new CancellationTokenSource();
        _sender.OnSend = () => shutdown.Cancel();
        _sender.Script(new OperationCanceledException(shutdown.Token));

        var act = () => RunBatchWithTokenAsync(shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var deliveries = _store.AllDeliveries.OrderBy(d => d.Priority).ToList();
        deliveries[1].Status.Should().Be(DeliveryStatus.Sending, "the interrupted send's outcome is unknown, so it is left for the sweeper");
        deliveries[0].Status.Should().Be(DeliveryStatus.Pending, "the delivery that never reached the sender goes back to the queue");
        deliveries[0].AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task HostShutdownAfterTheSendSucceeded_StillRecordsTheSend_SoADeliveredEmailIsNeverLeftAmbiguous()
    {
        Enqueue();
        using var shutdown = new CancellationTokenSource();
        _sender.OnSend = () => shutdown.Cancel(); // the host begins to stop while the message is being handed over
        _sender.Script(ChannelSendResult.Sent("SMTP accepted", Guid.CreateVersion7(), "en"));

        var act = () => RunBatchWithTokenAsync(shutdown.Token);

        // The pass then stops (the loop checks the token), but only after the outcome was written.
        await act.Should().ThrowAsync<OperationCanceledException>();
        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.Sent);
        _store.Saves.Should().Be(1);
    }

    [Fact]
    public async Task HostShutdownBeforeTheSenderTransmits_GivesTheDeliveryBackToTheQueue_BecauseNothingLeftTheProcess()
    {
        Enqueue();
        using var shutdown = new CancellationTokenSource();
        _sender.MarksTransmission = false; // the sender was cancelled while still rendering
        _sender.OnSend = () => shutdown.Cancel();
        _sender.Script(new OperationCanceledException(shutdown.Token));

        var act = () => RunBatchWithTokenAsync(shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var delivery = _store.Delivery();
        delivery.Status.Should().Be(DeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(0, "the claim's attempt is returned: nothing was sent");
    }

    [Fact]
    public async Task ReleaseUnstarted_WithNothingHeld_ReleasesNothing_AsStopAsyncRelyingOnItWhenTheLoopNeverRan()
    {
        var released = await Service.ReleaseUnstartedAsync(WorkerId, CancellationToken.None);

        released.Should().Be(0);
        _store.ReleasedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task DeliveryAlreadyClaimedByAnotherWorker_IsNotClaimedAgain()
    {
        Enqueue();
        var batchTask = _store.ClaimDueDeliveriesAsync("someone-else", [NotificationChannel.Email], Now.AddMinutes(2), 10, Now, CancellationToken.None);
        await batchTask;

        var batch = await RunBatchAsync();

        batch.Claimed.Should().Be(0);
        _sender.Contexts.Should().BeEmpty();
    }

    // ---- a scripted channel sender ----

    private sealed class FakeChannelSender : INotificationChannelSender
    {
        private readonly Queue<object> _script = new();

        public NotificationChannel Channel => NotificationChannel.Email;

        public List<DeliveryContext> Contexts { get; } = [];

        public Action? OnSend { get; set; }

        /// <summary>Queues a result, or an exception to throw. With nothing queued a send succeeds.</summary>
        public void Script(object resultOrException) => _script.Enqueue(resultOrException);

        /// <summary>Whether the fake reports "transmission started" before it acts, as the email sender does.</summary>
        public bool MarksTransmission { get; set; } = true;

        public Task<ChannelSendResult> SendAsync(DeliveryContext context, CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            if (MarksTransmission)
            {
                context.Progress?.MarkTransmissionStarted();
            }

            OnSend?.Invoke();
            if (_script.Count == 0)
            {
                return Task.FromResult(ChannelSendResult.Sent("SMTP accepted", Guid.CreateVersion7(), context.Language));
            }

            return _script.Dequeue() switch
            {
                Exception ex => throw ex,
                ChannelSendResult result => Task.FromResult(result),
                var other => throw new InvalidOperationException($"Unscriptable {other.GetType().Name}."),
            };
        }
    }
}
