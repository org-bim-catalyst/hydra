using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Application.Notifications.Commands.DeleteNotification;
using AskLucy.Application.Notifications.Commands.MarkAllNotificationsRead;
using AskLucy.Application.Notifications.Commands.MarkNotificationRead;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace AskLucy.Application.Tests.Notifications;

/// <summary>
/// specs/067 T057 — mark-read, mark-all-read and delete against a substitute repository/publisher:
/// the realtime pushes contracts/notification-hub.md promises, and their idempotent/no-op edges.
/// </summary>
public sealed class NotificationCenterHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
    private const string UserId = "user-1";

    private readonly INotificationRepository _repository = Substitute.For<INotificationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly INotificationAuditWriter _audit = Substitute.For<INotificationAuditWriter>();
    private readonly INotificationRealtimePublisher _realtime = Substitute.For<INotificationRealtimePublisher>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly FakeTimeProvider _timeProvider = new(Now);

    public NotificationCenterHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
    }

    private static Notification UnreadNotification(string userId = UserId)
    {
        var notification = Notification.Create(
            userId, NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed), NotificationPriority.High,
            "Workflow failed", "Message", "en", "corr-1", Now.UtcDateTime, showInCenter: true);
        notification.AddDelivery(NotificationDelivery.CreateDelivered(
            NotificationChannel.InApp, NotificationPriority.High, "en", null, "corr-1", Now.UtcDateTime));
        return notification;
    }

    // --- MarkNotificationReadCommandHandler ---

    [Fact]
    public async Task MarkRead_OnAnUnreadNotification_MarksItAndPushesNotificationUpdated_WithTheFreshUnreadCount()
    {
        var notification = UnreadNotification();
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        _repository.CountUnreadAsync(UserId, Arg.Any<CancellationToken>()).Returns(4);
        var handler = new MarkNotificationReadCommandHandler(
            _repository, _unitOfWork, _audit, _realtime, _currentUser, _timeProvider, NullLogger<MarkNotificationReadCommandHandler>.Instance);

        await handler.Handle(new MarkNotificationReadCommand(notification.Id), TestContext.Current.CancellationToken);

        notification.IsRead.Should().BeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _realtime.Received(1).NotificationUpdatedAsync(UserId, notification.Id, NotificationChange.Read, 4, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkRead_OnAnUnreadApprovalRequest_WritesOneReadAuditRow_WithTheNotificationsCorrelationId()
    {
        var notification = Notification.Create(
            UserId, NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowApprovalRequested), NotificationPriority.High,
            "Approval needed", "Message", "en", "corr-approval", Now.UtcDateTime, showInCenter: true);
        notification.AddDelivery(NotificationDelivery.CreateDelivered(
            NotificationChannel.InApp, NotificationPriority.High, "en", null, "corr-approval", Now.UtcDateTime));
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        var handler = new MarkNotificationReadCommandHandler(
            _repository, _unitOfWork, _audit, _realtime, _currentUser, _timeProvider, NullLogger<MarkNotificationReadCommandHandler>.Instance);

        await handler.Handle(new MarkNotificationReadCommand(notification.Id), TestContext.Current.CancellationToken);
        await handler.Handle(new MarkNotificationReadCommand(notification.Id), TestContext.Current.CancellationToken);

        _audit.Received(1).Write(
            NotificationAuditAction.ApprovalNotificationRead, "Notification", notification.Id.ToString(),
            NotificationAuditOutcome.Succeeded, Arg.Any<object?>(), "corr-approval");
    }

    [Fact]
    public async Task MarkRead_OnAnOrdinaryNotification_WritesNoAuditRow()
    {
        var notification = UnreadNotification();
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        var handler = new MarkNotificationReadCommandHandler(
            _repository, _unitOfWork, _audit, _realtime, _currentUser, _timeProvider, NullLogger<MarkNotificationReadCommandHandler>.Instance);

        await handler.Handle(new MarkNotificationReadCommand(notification.Id), TestContext.Current.CancellationToken);

        _audit.DidNotReceiveWithAnyArgs().Write(default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task MarkRead_CalledTwice_IsIdempotent_AndOnlyPushesOnce()
    {
        var notification = UnreadNotification();
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        _repository.CountUnreadAsync(UserId, Arg.Any<CancellationToken>()).Returns(4);
        var handler = new MarkNotificationReadCommandHandler(
            _repository, _unitOfWork, _audit, _realtime, _currentUser, _timeProvider, NullLogger<MarkNotificationReadCommandHandler>.Instance);

        await handler.Handle(new MarkNotificationReadCommand(notification.Id), TestContext.Current.CancellationToken);
        await handler.Handle(new MarkNotificationReadCommand(notification.Id), TestContext.Current.CancellationToken);

        // SaveChangesAsync still runs each time (MarkRead is a no-op the second time, but the
        // handler doesn't special-case that) — the push is the part that must not repeat.
        await _realtime.Received(1).NotificationUpdatedAsync(UserId, notification.Id, NotificationChange.Read, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkRead_APushFailure_DoesNotFailTheRequest()
    {
        var notification = UnreadNotification();
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        _repository.CountUnreadAsync(UserId, Arg.Any<CancellationToken>()).Returns(4);
        _realtime.NotificationUpdatedAsync(UserId, notification.Id, NotificationChange.Read, 4, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("hub unreachable"));
        var handler = new MarkNotificationReadCommandHandler(
            _repository, _unitOfWork, _audit, _realtime, _currentUser, _timeProvider, NullLogger<MarkNotificationReadCommandHandler>.Instance);

        var act = () => handler.Handle(new MarkNotificationReadCommand(notification.Id), TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    // --- MarkAllNotificationsReadCommandHandler ---

    [Fact]
    public async Task MarkAllRead_WhenRowsWereUpdated_PushesUnreadCountChanged_ExactlyOnce()
    {
        _repository.MarkAllReadAsync(UserId, null, Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(3);
        _repository.CountUnreadAsync(UserId, Arg.Any<CancellationToken>()).Returns(0);
        var handler = new MarkAllNotificationsReadCommandHandler(
            _repository, _realtime, _currentUser, _timeProvider, NullLogger<MarkAllNotificationsReadCommandHandler>.Instance);

        var updated = await handler.Handle(new MarkAllNotificationsReadCommand(), TestContext.Current.CancellationToken);

        updated.Should().Be(3);
        await _realtime.Received(1).UnreadCountChangedAsync(UserId, 0, Arg.Any<CancellationToken>());
        await _realtime.DidNotReceive().NotificationUpdatedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<NotificationChange>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkAllRead_WhenNothingWasUnread_UpdatesNothing_AndNeverPushes()
    {
        _repository.MarkAllReadAsync(UserId, null, Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(0);
        var handler = new MarkAllNotificationsReadCommandHandler(
            _repository, _realtime, _currentUser, _timeProvider, NullLogger<MarkAllNotificationsReadCommandHandler>.Instance);

        var updated = await handler.Handle(new MarkAllNotificationsReadCommand(), TestContext.Current.CancellationToken);

        updated.Should().Be(0);
        await _realtime.DidNotReceiveWithAnyArgs().UnreadCountChangedAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MarkAllRead_ScopedToACategory_PassesItToTheRepository()
    {
        _repository.MarkAllReadAsync(UserId, NotificationCategory.Document, Now.UtcDateTime, Arg.Any<CancellationToken>()).Returns(1);
        var handler = new MarkAllNotificationsReadCommandHandler(
            _repository, _realtime, _currentUser, _timeProvider, NullLogger<MarkAllNotificationsReadCommandHandler>.Instance);

        await handler.Handle(new MarkAllNotificationsReadCommand(NotificationCategory.Document), TestContext.Current.CancellationToken);

        await _repository.Received(1).MarkAllReadAsync(UserId, NotificationCategory.Document, Now.UtcDateTime, Arg.Any<CancellationToken>());
    }

    // --- DeleteNotificationCommandHandler ---

    [Fact]
    public async Task Delete_SoftDeletesAndPushesNotificationUpdated_WithDeletedChange()
    {
        var notification = UnreadNotification();
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        _repository.CountUnreadAsync(UserId, Arg.Any<CancellationToken>()).Returns(2);
        var handler = new DeleteNotificationCommandHandler(
            _repository, _unitOfWork, _realtime, _currentUser, _timeProvider, NullLogger<DeleteNotificationCommandHandler>.Instance);

        await handler.Handle(new DeleteNotificationCommand(notification.Id), TestContext.Current.CancellationToken);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _realtime.Received(1).NotificationUpdatedAsync(UserId, notification.Id, NotificationChange.Deleted, 2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_OwnedByAnotherUser_ThrowsKeyNotFound_AndNeverTouchesTheRepositoryAgain()
    {
        var notification = UnreadNotification(userId: "someone-else");
        _repository.GetByIdAsync(notification.Id, Arg.Any<CancellationToken>()).Returns(notification);
        var handler = new DeleteNotificationCommandHandler(
            _repository, _unitOfWork, _realtime, _currentUser, _timeProvider, NullLogger<DeleteNotificationCommandHandler>.Instance);

        var act = () => handler.Handle(new DeleteNotificationCommand(notification.Id), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
        await _realtime.DidNotReceiveWithAnyArgs().NotificationUpdatedAsync(default!, default, default, default, TestContext.Current.CancellationToken);
    }
}
