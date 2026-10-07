using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Admin;
using AskLucy.Application.Notifications.Commands.BulkRetryNotificationDeliveries;
using AskLucy.Application.Notifications.Commands.RetryNotificationDelivery;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>T152 (specs/067 US6) — an administrator's retry of one delivery or many: the four refusals, the reset, and the audit rows.</summary>
public sealed class RetryDeliveryHandlerTests
{
    private const string UserId = "user-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly INotificationAdminRepository _admin = Substitute.For<INotificationAdminRepository>();
    private readonly INotificationRecipientDirectory _directory = Substitute.For<INotificationRecipientDirectory>();
    private readonly INotificationAuditWriter _audit = Substitute.For<INotificationAuditWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _time = new(Now);

    public RetryDeliveryHandlerTests()
    {
        SetAccount(isActive: true);
    }

    private void SetAccount(bool isActive) =>
        _directory.GetAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<string, NotificationRecipientInfo>
        {
            [UserId] = new(UserId, "Layla", "layla@example.com", EmailConfirmed: true, IsActive: isActive),
        });

    private RetryNotificationDeliveryCommandHandler SingleHandler() => new(_notifications, _directory, _audit, _unitOfWork, _time);

    private BulkRetryNotificationDeliveriesCommandHandler BulkHandler() => new(_notifications, _admin, _directory, _audit, _unitOfWork, _time);

    /// <summary>A notification with one failed email delivery.</summary>
    private Notification FailedNotification(DateTime? expiresAtUtc = null, bool dead = false)
    {
        var now = Now.UtcDateTime;
        var notification = Notification.Create(
            UserId, NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed), NotificationPriority.High,
            "Workflow failed", "Message", "en", "corr-1", now, showInCenter: true, expiresAtUtc: expiresAtUtc);
        var delivery = NotificationDelivery.CreatePending(NotificationChannel.Email, NotificationPriority.High, RecipientKind.User, null, 5, null, "corr-1", now);
        notification.AddDelivery(delivery);
        delivery.MarkSending("worker-1", now.AddMinutes(2), now);
        if (dead)
        {
            delivery.DeadLetter("Retry limit reached.");
        }
        else
        {
            delivery.Fail(DeliveryFailureKind.Permanent, "Rejected.", "550", now);
        }

        _notifications.GetByDeliveryIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => [notification]);
        return notification;
    }

    private static Guid DeliveryOf(Notification notification) => notification.Deliveries.Single().Id;

    // ---- single retry ----

    [Fact]
    public async Task Retry_OfAFailedDelivery_ResetsItToPending_AndAuditsIt()
    {
        var notification = FailedNotification();
        var id = DeliveryOf(notification);

        var result = await SingleHandler().Handle(new RetryNotificationDeliveryCommand(id), TestContext.Current.CancellationToken);

        result.Should().Be(new RetryNotificationDeliveryResult(id, DeliveryStatus.Pending));
        var delivery = notification.Deliveries.Single();
        delivery.Status.Should().Be(DeliveryStatus.Pending);
        delivery.AttemptCount.Should().Be(0);
        delivery.FailureReason.Should().BeNull();
        _audit.Received(1).Write(NotificationAuditAction.DeliveryRetried, "NotificationDelivery", id.ToString(), NotificationAuditOutcome.Succeeded, Arg.Any<object?>(), Arg.Any<string?>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retry_OfADeadLetteredDelivery_ResetsItToo()
    {
        var notification = FailedNotification(dead: true);

        await SingleHandler().Handle(new RetryNotificationDeliveryCommand(DeliveryOf(notification)), TestContext.Current.CancellationToken);

        notification.Deliveries.Single().Status.Should().Be(DeliveryStatus.Pending);
    }

    [Fact]
    public async Task Retry_OfADeliveryThatDoesNotExist_IsNotFound()
    {
        _notifications.GetByDeliveryIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);

        var act = () => SingleHandler().Handle(new RetryNotificationDeliveryCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Retry_OfADeliveryThatIsNotFailed_IsRefused_NotFailed()
    {
        var notification = FailedNotification();
        var delivery = notification.Deliveries.Single();
        notification.RetryDelivery(delivery.Id, Now.UtcDateTime); // now Pending

        var act = () => SingleHandler().Handle(new RetryNotificationDeliveryCommand(delivery.Id), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<DeliveryRetryRefusedException>()).Which.Reason.Should().Be(DeliveryRetryRefusal.NotFailed);
    }

    [Fact]
    public async Task Retry_OfADeletedNotification_IsRefused_NotificationDeleted()
    {
        var notification = FailedNotification();
        var delivery = notification.Deliveries.Single();
        // The owner deleted it from the center: reading it needs an in-app delivery, so the notification is marked deleted directly.
        notification.GetType().GetProperty(nameof(Notification.DeletedAtUtc))!.SetValue(notification, Now.UtcDateTime);

        var act = () => SingleHandler().Handle(new RetryNotificationDeliveryCommand(delivery.Id), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<DeliveryRetryRefusedException>()).Which.Reason.Should().Be(DeliveryRetryRefusal.NotificationDeleted);
    }

    [Fact]
    public async Task Retry_OfAnExpiredNotification_IsRefused_NotificationExpired()
    {
        var notification = FailedNotification(expiresAtUtc: Now.UtcDateTime.AddMinutes(-1));

        var act = () => SingleHandler().Handle(new RetryNotificationDeliveryCommand(DeliveryOf(notification)), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<DeliveryRetryRefusedException>()).Which.Reason.Should().Be(DeliveryRetryRefusal.NotificationExpired);
    }

    [Fact]
    public async Task Retry_ForARecipientWhoseAccountIsGone_IsRefused_RecipientDeleted()
    {
        var notification = FailedNotification();
        SetAccount(isActive: false);

        var act = () => SingleHandler().Handle(new RetryNotificationDeliveryCommand(DeliveryOf(notification)), TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<DeliveryRetryRefusedException>()).Which.Reason.Should().Be(DeliveryRetryRefusal.RecipientDeleted);
    }

    [Fact]
    public async Task ARefusedRetry_ChangesNothing_AndWritesNoAuditRow()
    {
        var notification = FailedNotification(expiresAtUtc: Now.UtcDateTime.AddMinutes(-1));

        await SingleHandler().Invoking(h => h.Handle(new RetryNotificationDeliveryCommand(DeliveryOf(notification)), TestContext.Current.CancellationToken))
            .Should().ThrowAsync<DeliveryRetryRefusedException>();

        notification.Deliveries.Single().Status.Should().Be(DeliveryStatus.Failed);
        _audit.DidNotReceiveWithAnyArgs().Write(default, default!, default!, default, default, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- bulk retry ----

    [Fact]
    public async Task BulkRetry_ByIds_ReportsRequestedRetriedAndSkipped_AndAuditsOnce()
    {
        var retryable = FailedNotification();
        var expired = FailedNotification(expiresAtUtc: Now.UtcDateTime.AddMinutes(-1));
        var retryableId = DeliveryOf(retryable);
        var expiredId = DeliveryOf(expired);
        _notifications.GetByDeliveryIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([retryable, expired]);

        var result = await BulkHandler().Handle(new BulkRetryNotificationDeliveriesCommand(DeliveryIds: [retryableId, expiredId]), TestContext.Current.CancellationToken);

        result.Requested.Should().Be(2);
        result.Retried.Should().Be(1);
        result.Skipped.Should().ContainSingle().Which.Should().Be(new SkippedDelivery(expiredId, DeliveryRetryRefusal.NotificationExpired));
        retryable.Deliveries.Single().Status.Should().Be(DeliveryStatus.Pending);
        expired.Deliveries.Single().Status.Should().Be(DeliveryStatus.Failed);
        _audit.Received(1).Write(NotificationAuditAction.DeliveriesBulkRetried, "NotificationDelivery", "bulk", NotificationAuditOutcome.Succeeded, Arg.Any<object?>(), Arg.Any<string?>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BulkRetry_OfAnIdWithNoDelivery_IsSkipped_NotSilentlyDropped()
    {
        _notifications.GetByDeliveryIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
        var missing = Guid.NewGuid();

        var result = await BulkHandler().Handle(new BulkRetryNotificationDeliveriesCommand(DeliveryIds: [missing]), TestContext.Current.CancellationToken);

        result.Requested.Should().Be(1);
        result.Retried.Should().Be(0);
        result.Skipped.Should().ContainSingle().Which.Should().Be(new SkippedDelivery(missing, DeliveryRetryRefusal.NotificationDeleted));
    }

    [Fact]
    public async Task BulkRetry_ByFilter_RetriesWhatTheFilterMatches()
    {
        var notification = FailedNotification(dead: true);
        _admin.FindDeliveryIdsAsync(Arg.Any<AdminDeliveryFilter>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([DeliveryOf(notification)]);

        var result = await BulkHandler().Handle(
            new BulkRetryNotificationDeliveriesCommand(Filter: new BulkRetryFilter([DeliveryStatus.DeadLettered], NotificationChannel.Email)),
            TestContext.Current.CancellationToken);

        result.Requested.Should().Be(1);
        result.Retried.Should().Be(1);
        result.Skipped.Should().BeEmpty();
        await _admin.Received(1).FindDeliveryIdsAsync(
            Arg.Is<AdminDeliveryFilter>(f => f != null && f.Channel == NotificationChannel.Email && f.Statuses.SequenceEqual(new[] { DeliveryStatus.DeadLettered })),
            BulkRetryNotificationDeliveriesCommand.MaxMatches + 1,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BulkRetry_ByFilter_ThatMatchesMoreThanAThousand_IsRefusedWhole()
    {
        _admin.FindDeliveryIdsAsync(Arg.Any<AdminDeliveryFilter>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid()).ToList());

        var act = () => BulkHandler().Handle(new BulkRetryNotificationDeliveriesCommand(Filter: new BulkRetryFilter()), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<FluentValidation.ValidationException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void BulkRetryValidator_AcceptsOneToTwoHundredIds(int count, bool valid)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

        new BulkRetryNotificationDeliveriesCommandValidator().Validate(new BulkRetryNotificationDeliveriesCommand(DeliveryIds: ids)).IsValid.Should().Be(valid);
    }

    [Fact]
    public void BulkRetryValidator_NeedsExactlyOneOfIdsAndFilter()
    {
        var validator = new BulkRetryNotificationDeliveriesCommandValidator();

        validator.Validate(new BulkRetryNotificationDeliveriesCommand()).IsValid.Should().BeFalse();
        validator.Validate(new BulkRetryNotificationDeliveriesCommand([Guid.NewGuid()], new BulkRetryFilter())).IsValid.Should().BeFalse();
        validator.Validate(new BulkRetryNotificationDeliveriesCommand(Filter: new BulkRetryFilter())).IsValid.Should().BeTrue();
    }

    [Fact]
    public void BulkRetryValidator_RefusesStatusesThatCantBeRetried()
    {
        var command = new BulkRetryNotificationDeliveriesCommand(Filter: new BulkRetryFilter([DeliveryStatus.Sent]));

        new BulkRetryNotificationDeliveriesCommandValidator().Validate(command).IsValid.Should().BeFalse();
    }
}
