using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Documents;
using AskLucy.Infrastructure.Documents;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace AskLucy.Web.Tests.Documents;

/// <summary>
/// T065 (updated specs/067 T082) — <see cref="ProcessingNotifier"/> targets only the owning user's
/// SignalR group (<see cref="DocumentProcessingHub.UserGroup"/>) for the near-real-time stage/progress
/// events — the other half of the group-isolation guarantee alongside
/// <see cref="DocumentProcessingHubTests"/> — and routes the notification-hub events through
/// <see cref="INotificationPublisher"/> rather than persisting a <see cref="DocumentNotification"/>
/// row itself (the dispatcher/materializer owns that from here on).
/// </summary>
public sealed class ProcessingNotifierTests
{
    private readonly INotificationPublisher _publisher = Substitute.For<INotificationPublisher>();
    private readonly IHubClients _hubClients = Substitute.For<IHubClients>();
    private readonly IClientProxy _ownerGroupProxy = Substitute.For<IClientProxy>();

    private ProcessingNotifier CreateSut(string ownerUserId)
    {
        _hubClients.Group(DocumentProcessingHub.UserGroup(ownerUserId)).Returns(_ownerGroupProxy);
        var hubContext = Substitute.For<IHubContext<DocumentProcessingHub>>();
        hubContext.Clients.Returns(_hubClients);
        return new ProcessingNotifier(hubContext, _publisher);
    }

    [Fact]
    public async Task NotifyStageChangedAsync_ShouldSendOnlyToTheOwningUsersGroup()
    {
        var sut = CreateSut("user-1");

        await sut.NotifyStageChangedAsync(
            "user-1", Guid.CreateVersion7(), DocumentProcessingStageType.Ocr, DocumentProcessingStageStatus.Completed, CancellationToken.None);

        await _ownerGroupProxy.Received(1).SendCoreAsync("documentStageChanged", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyProcessingCompletedAsync_ShouldSendOnlyToTheOwningUsersGroup()
    {
        var sut = CreateSut("user-1");

        await sut.NotifyProcessingCompletedAsync("user-1", Guid.CreateVersion7(), CancellationToken.None);

        await _ownerGroupProxy.Received(1).SendCoreAsync("documentProcessingCompleted", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyProcessingFailedAsync_ShouldSendOnlyToTheOwningUsersGroup()
    {
        var sut = CreateSut("user-2");

        await sut.NotifyProcessingFailedAsync("user-2", Guid.CreateVersion7(), "Corrupted file.", CancellationToken.None);

        await _ownerGroupProxy.Received(1).SendCoreAsync("documentProcessingFailed", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotifyAsync_ShouldPublishANotificationRequestForTheOwningUser()
    {
        var sut = CreateSut("user-3");
        var documentId = Guid.CreateVersion7();

        await sut.NotifyAsync(
            "user-3", DocumentNotificationEventType.ProcessingCompleted, documentId, "dedupe-1",
            documentName: "Report.pdf", cancellationToken: TestContext.Current.CancellationToken);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r =>
            ((NotificationRecipient.User)r!.Recipient).UserId == "user-3" &&
            r.RelatedItem == new RelatedItem("Document", documentId.ToString()) &&
            r.EventKey == $"document:{documentId}:{DocumentNotificationEventType.ProcessingCompleted}:dedupe-1"));
    }

    [Fact]
    public async Task NotifyOcrCompletedAsync_ShouldPublishANotificationRequestForTheOwningUser()
    {
        var sut = CreateSut("user-4");
        var documentId = Guid.CreateVersion7();

        await sut.NotifyOcrCompletedAsync("user-4", documentId, "Report.pdf", "dedupe-2", TestContext.Current.CancellationToken);

        _publisher.Received(1).Publish(Arg.Is<NotificationRequest>(r =>
            ((NotificationRecipient.User)r!.Recipient).UserId == "user-4" &&
            r.RelatedItem == new RelatedItem("Document", documentId.ToString()) &&
            r.EventKey == $"document:{documentId}:ocr-completed:dedupe-2"));
    }
}
