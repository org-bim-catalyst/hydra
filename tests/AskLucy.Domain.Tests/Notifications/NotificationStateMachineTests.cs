using AskLucy.Domain.Common;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Xunit;

namespace AskLucy.Domain.Tests.Notifications;

/// <summary>data-model.md § State machines.</summary>
public sealed class NotificationStateMachineTests
{
    private const string UserId = "user-1";
    private const string CorrelationId = "corr-1";
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private static Notification NewNotification(string type = NotificationTypeKeys.WorkflowExecutionFailed) =>
        Notification.Create(UserId, NotificationTypeCatalog.Get(type), NotificationPriority.High, "Title", "Message", "en", CorrelationId, Now, showInCenter: true);

    private static NotificationDelivery InApp() =>
        NotificationDelivery.CreateDelivered(NotificationChannel.InApp, NotificationPriority.High, "en", null, CorrelationId, Now);

    private static NotificationDelivery PendingEmail(int maxAttempts = 3) =>
        NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.High, RecipientKind.User, null, maxAttempts, null, CorrelationId, Now);

    // --- Notification.Status ---

    [Fact]
    public void Notification_WithInAppDelivered_IsDelivered_AndCanBeRead()
    {
        var notification = NewNotification();
        notification.AddDelivery(InApp());
        notification.AddDelivery(PendingEmail());

        notification.Status.Should().Be(NotificationStatus.Delivered);
        notification.MarkRead(Now);
        notification.Status.Should().Be(NotificationStatus.Read);
    }

    [Fact]
    public void MarkRead_IsIdempotent_AndReadNeverRegresses()
    {
        var notification = NewNotification();
        notification.AddDelivery(InApp());
        notification.MarkRead(Now);

        notification.MarkRead(Now.AddMinutes(1));
        notification.RecomputeStatus();

        notification.ReadAtUtc.Should().Be(Now);
        notification.Status.Should().Be(NotificationStatus.Read);
    }

    [Fact]
    public void MarkRead_Throws_BeforeTheNotificationReachedTheRecipient()
    {
        var notification = NewNotification();
        notification.AddDelivery(PendingEmail());

        notification.Status.Should().Be(NotificationStatus.Queued);
        var act = () => notification.MarkRead(Now);
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Status_FollowsTheBestEmailOutcome()
    {
        var notification = Notification.Create(null, NotificationTypeCatalog.Get(NotificationTypeKeys.AccountPasswordResetRequested), NotificationPriority.Critical, string.Empty, string.Empty, "en", CorrelationId, Now, showInCenter: false);
        var email = NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.Critical, RecipientKind.Address, "a@example.com", 3, null, CorrelationId, Now);
        notification.AddDelivery(email);
        notification.Status.Should().Be(NotificationStatus.Queued);

        email.MarkSending("w1", Now.AddMinutes(2), Now);
        notification.RecomputeStatus();
        notification.Status.Should().Be(NotificationStatus.Processing);

        email.MarkSent("en", null, "250 OK", Now);
        notification.RecomputeStatus();
        notification.Status.Should().Be(NotificationStatus.Sent);
    }

    [Fact]
    public void Status_IsFailedOnlyWhenEveryNonSkippedDeliveryFailed()
    {
        var notification = NewNotification();
        var email = PendingEmail();
        notification.AddDelivery(email);
        notification.AddDelivery(NotificationDelivery.CreateSkipped(NotificationChannel.InApp, NotificationPriority.High, RecipientKind.User, DeliverySkipReason.PreferenceDisabled, CorrelationId, Now));

        email.Fail(DeliveryFailureKind.Permanent, "Rejected", "550", Now);
        notification.RecomputeStatus();

        notification.Status.Should().Be(NotificationStatus.Failed);
    }

    [Fact]
    public void Status_IsCancelled_WhenEveryChannelWasSkipped()
    {
        var notification = NewNotification();
        notification.AddDelivery(NotificationDelivery.CreateSkipped(NotificationChannel.Email, NotificationPriority.High, RecipientKind.User, DeliverySkipReason.NoVerifiedAddress, CorrelationId, Now));

        notification.Status.Should().Be(NotificationStatus.Cancelled);
    }

    [Fact]
    public void Expire_ExpiresWaitingDeliveries()
    {
        var notification = NewNotification();
        notification.AddDelivery(PendingEmail());

        notification.Expire(Now);

        notification.Deliveries.Single().Status.Should().Be(DeliveryStatus.Expired);
        notification.Status.Should().Be(NotificationStatus.Expired);
    }

    [Fact]
    public void Cancel_CancelsWaitingDeliveries()
    {
        var notification = NewNotification();
        notification.AddDelivery(PendingEmail());

        notification.Cancel("Recipient deleted");

        notification.Status.Should().Be(NotificationStatus.Cancelled);
    }

    [Fact]
    public void AddDelivery_RejectsASecondDeliveryOnTheSameChannel()
    {
        var notification = NewNotification();
        notification.AddDelivery(InApp());

        var act = () => notification.AddDelivery(InApp());

        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void DeleteByOwner_RejectsANonOwner_AndIsIdempotentForTheOwner()
    {
        var notification = NewNotification();

        var act = () => notification.DeleteByOwner("someone-else", Now);
        act.Should().Throw<DomainRuleViolationException>();

        notification.DeleteByOwner(UserId, Now);
        notification.DeleteByOwner(UserId, Now.AddMinutes(1));
        notification.DeletedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Create_RequiresARecipientAndCopy_WhenShownInTheCenter()
    {
        var definition = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed);

        var noRecipient = () => Notification.Create(null, definition, NotificationPriority.High, "T", "M", "en", CorrelationId, Now, showInCenter: true);
        var noTitle = () => Notification.Create(UserId, definition, NotificationPriority.High, " ", "M", "en", CorrelationId, Now, showInCenter: true);

        noRecipient.Should().Throw<DomainRuleViolationException>();
        noTitle.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Create_NeverShowsAnEmailOnlyTypeInTheCenter()
    {
        var notification = Notification.Create(UserId, NotificationTypeCatalog.Get(NotificationTypeKeys.AccountPasswordResetRequested), NotificationPriority.Critical, string.Empty, string.Empty, "en", CorrelationId, Now, showInCenter: true);

        notification.ShowInCenter.Should().BeFalse();
    }

    // --- NotificationDelivery.Status ---

    [Fact]
    public void Delivery_RetriesThenDeadLetters_WhenAttemptsAreUsedUp()
    {
        var email = PendingEmail(maxAttempts: 2);

        email.MarkSending("w1", Now.AddMinutes(2), Now);
        email.ScheduleRetry(Now.AddMinutes(1), DeliveryFailureKind.Transient, "Timeout", null, Now);
        email.Status.Should().Be(DeliveryStatus.Retrying);
        email.LeaseOwner.Should().BeNull();

        email.MarkSending("w1", Now.AddMinutes(3), Now.AddMinutes(1));
        email.ScheduleRetry(Now.AddMinutes(5), DeliveryFailureKind.Transient, "Timeout", null, Now.AddMinutes(1));

        email.Status.Should().Be(DeliveryStatus.DeadLettered);
        email.FailureKind.Should().Be(DeliveryFailureKind.RetryLimitReached);
    }

    [Fact]
    public void Delivery_SweptWhileSending_FailsAsAmbiguous()
    {
        var email = PendingEmail();
        email.MarkSending("w1", Now.AddMinutes(2), Now);

        email.Fail(DeliveryFailureKind.AmbiguousOutcome, "Worker stopped mid-send", null, Now.AddMinutes(3));

        email.Status.Should().Be(DeliveryStatus.Failed);
        email.FailureKind.Should().Be(DeliveryFailureKind.AmbiguousOutcome);
    }

    [Fact]
    public void Delivery_ForbiddenTransitions_Throw()
    {
        var sent = PendingEmail();
        sent.MarkSending("w1", Now.AddMinutes(2), Now);
        sent.MarkSent("en", null, "250 OK", Now);

        new Action[]
        {
            () => sent.MarkSending("w1", Now, Now),
            () => sent.ScheduleRetry(Now, DeliveryFailureKind.Transient, "x", null, Now),
            () => sent.Fail(DeliveryFailureKind.Permanent, "x", null, Now),
            () => sent.Cancel("x"),
            () => sent.Expire(),
            () => sent.Skip(DeliverySkipReason.ChannelDisabled),
            () => PendingEmail().MarkSent("en", null, null, Now),
            () => PendingEmail().ScheduleRetry(Now, DeliveryFailureKind.Transient, "x", null, Now),
            () => InApp().MarkSending("w1", Now, Now),
        }.Should().AllSatisfy(act => act.Should().Throw<DomainRuleViolationException>());
    }

    [Fact]
    public void Delivery_AddressRules_AreEnforced()
    {
        var missing = () => NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.Normal, RecipientKind.Address, null, 3, null, CorrelationId, Now);
        var supportWithAddress = () => NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.Normal, RecipientKind.SupportMailbox, "support@example.com", 3, null, CorrelationId, Now);

        missing.Should().Throw<DomainRuleViolationException>();
        supportWithAddress.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void RetryDelivery_ResetsAFailedDelivery_ButNotForADeletedNotification()
    {
        var notification = NewNotification();
        var email = PendingEmail();
        notification.AddDelivery(InApp());
        notification.AddDelivery(email);
        email.MarkSending("w1", Now.AddMinutes(2), Now);
        email.Fail(DeliveryFailureKind.Permanent, "Rejected", null, Now);

        notification.RetryDelivery(email.Id, Now.AddMinutes(5));
        email.Status.Should().Be(DeliveryStatus.Pending);
        email.AttemptCount.Should().Be(0);

        email.MarkSending("w1", Now.AddMinutes(7), Now.AddMinutes(5));
        email.Fail(DeliveryFailureKind.Permanent, "Rejected", null, Now.AddMinutes(5));
        notification.DeleteByOwner(UserId, Now.AddMinutes(6));
        var act = () => notification.RetryDelivery(email.Id, Now.AddMinutes(7));
        act.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void RetryDelivery_IsRefused_ForASentDelivery()
    {
        var notification = NewNotification();
        var email = PendingEmail();
        notification.AddDelivery(email);
        email.MarkSending("w1", Now.AddMinutes(2), Now);
        email.MarkSent("en", null, "250", Now);

        var act = () => notification.RetryDelivery(email.Id, Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    // --- NotificationTemplateVersion.Status ---

    private static readonly NotificationTemplateContent InAppContent = new() { Title = "{{ workflowName }} failed", Message = "Because {{ failureSummary }}." };

    [Fact]
    public void Template_PublishArchivesThePreviousVersion()
    {
        var template = NotificationTemplate.Create(NotificationTypeKeys.WorkflowExecutionFailed, NotificationChannel.InApp, "en", "Workflow failed", Now);
        var v1 = template.AddDraft(InAppContent, Now);
        template.Publish(v1.Id, UserId, Now);
        var v2 = template.AddDraft(InAppContent with { Title = "Failed: {{ workflowName }}" }, Now);

        template.Publish(v2.Id, UserId, Now);

        v1.Status.Should().Be(TemplateVersionStatus.Archived);
        v2.Status.Should().Be(TemplateVersionStatus.Published);
        v2.VersionNumber.Should().Be(2);
        template.PublishedVersionId.Should().Be(v2.Id);
    }

    [Fact]
    public void Template_OnlyDraftsAreEditable_AndPublishedCantBePublishedAgain()
    {
        var template = NotificationTemplate.Create(NotificationTypeKeys.WorkflowExecutionFailed, NotificationChannel.InApp, "en", "Workflow failed", Now);
        var v1 = template.AddDraft(InAppContent, Now);
        template.Publish(v1.Id, UserId, Now);

        var edit = () => template.UpdateDraft(v1.Id, InAppContent);
        var republish = () => template.Publish(v1.Id, UserId, Now);

        edit.Should().Throw<DomainRuleViolationException>();
        republish.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Template_CantArchiveTheLastPublishedVersionOfAShippedDefault_ButCanArchiveADraft()
    {
        var template = NotificationTemplate.Create(NotificationTypeKeys.WorkflowExecutionFailed, NotificationChannel.InApp, "en", "Workflow failed", Now);
        var v1 = template.AddDraft(InAppContent, Now);
        template.Publish(v1.Id, UserId, Now);
        var draft = template.AddDraft(InAppContent, Now);

        var archivePublished = () => template.Archive(v1.Id, UserId, Now);
        archivePublished.Should().Throw<DomainRuleViolationException>();

        template.Archive(draft.Id, UserId, Now);
        draft.Status.Should().Be(TemplateVersionStatus.Archived);
        var archiveAgain = () => template.Archive(draft.Id, UserId, Now);
        archiveAgain.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Template_RejectsUndeclaredVariables_AndChannelMismatchedFields()
    {
        var template = NotificationTemplate.Create(NotificationTypeKeys.WorkflowExecutionFailed, NotificationChannel.InApp, "en", "Workflow failed", Now);

        var undeclared = () => template.AddDraft(InAppContent with { Message = "{{ password }}" }, Now);
        var emailField = () => template.AddDraft(InAppContent with { Subject = "Hi" }, Now);

        undeclared.Should().Throw<DomainRuleViolationException>();
        emailField.Should().Throw<DomainRuleViolationException>();
    }

    [Fact]
    public void Template_RecordsTheVariablesItUses()
    {
        var template = NotificationTemplate.Create(NotificationTypeKeys.WorkflowExecutionFailed, NotificationChannel.InApp, "en", "Workflow failed", Now);

        var version = template.AddDraft(InAppContent, Now);

        version.UsedVariables.Should().Equal("workflowName", "failureSummary");
    }

    [Fact]
    public void Template_CantBeCreatedForAChannelTheTypeDoesNotUse()
    {
        var act = () => NotificationTemplate.Create(NotificationTypeKeys.AccountPasswordResetRequested, NotificationChannel.InApp, "en", "Reset", Now);

        act.Should().Throw<DomainRuleViolationException>();
    }

    // --- NotificationOutboxEvent.Status ---

    private static NotificationOutboxEvent NewEvent() =>
        NotificationOutboxEvent.Create(NotificationTypeKeys.WorkflowExecutionFailed, "{\"users\":[\"user-1\"]}", "{}", CorrelationId, Now);

    [Fact]
    public void OutboxEvent_ClaimComplete()
    {
        var outboxEvent = NewEvent();

        outboxEvent.Claim("w1", Now.AddMinutes(2), Now);
        outboxEvent.Complete(OutboxEventOutcome.Materialized, Now);

        outboxEvent.Status.Should().Be(OutboxEventStatus.Completed);
        outboxEvent.Outcome.Should().Be(OutboxEventOutcome.Materialized);
        outboxEvent.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public void OutboxEvent_FailsAfterTheMaximumAttempts()
    {
        var outboxEvent = NewEvent();

        for (var i = 0; i < NotificationOutboxEvent.MaxDispatchAttempts; i++)
        {
            outboxEvent.Status.Should().Be(OutboxEventStatus.Pending);
            outboxEvent.Claim("w1", Now.AddMinutes(2), Now);
            outboxEvent.Release("boom", Now.AddMinutes(1), Now);
        }

        outboxEvent.Status.Should().Be(OutboxEventStatus.Failed);
    }

    [Fact]
    public void OutboxEvent_FanOutAdvancesTheCursor_WithoutUsingAnAttempt()
    {
        var outboxEvent = NewEvent();
        outboxEvent.Claim("w1", Now.AddMinutes(2), Now);

        outboxEvent.AdvanceFanOut("user-500", Now);

        outboxEvent.Status.Should().Be(OutboxEventStatus.Pending);
        outboxEvent.FanOutCursor.Should().Be("user-500");
        outboxEvent.Attempts.Should().Be(0);
    }

    [Fact]
    public void OutboxEvent_ForbiddenTransitions_Throw()
    {
        var claimed = NewEvent();
        claimed.Claim("w1", Now.AddMinutes(2), Now);

        new Action[]
        {
            () => NewEvent().Complete(OutboxEventOutcome.Materialized, Now),
            () => NewEvent().Release("x", Now, Now),
            () => claimed.Claim("w2", Now.AddMinutes(2), Now),
        }.Should().AllSatisfy(act => act.Should().Throw<DomainRuleViolationException>());
    }

    [Fact]
    public void OutboxEvent_ExpiredLease_IsReclaimable()
    {
        var outboxEvent = NewEvent();
        outboxEvent.Claim("w1", Now.AddMinutes(2), Now);

        outboxEvent.Claim("w2", Now.AddMinutes(5), Now.AddMinutes(3));

        outboxEvent.LeaseOwner.Should().Be("w2");
    }

    // --- NotificationPreference ---

    [Fact]
    public void Preference_RejectsLockedPairs_AndDigests()
    {
        var locked = () => NotificationPreference.Create(UserId, NotificationCategory.Security, NotificationChannel.Email, false, DeliveryFrequency.Immediate, Now);
        var digest = () => NotificationPreference.Create(UserId, NotificationCategory.Workflow, NotificationChannel.Email, true, DeliveryFrequency.DailyDigest, Now);

        locked.Should().Throw<DomainRuleViolationException>();
        digest.Should().Throw<DomainRuleViolationException>();
        NotificationPreference.Create(UserId, NotificationCategory.Workflow, NotificationChannel.Email, false, DeliveryFrequency.Immediate, Now)
            .IsEnabled.Should().BeFalse();
    }
}
