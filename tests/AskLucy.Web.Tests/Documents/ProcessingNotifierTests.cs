using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using AskLucy.Infrastructure.Documents;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace AskLucy.Web.Tests.Documents;

/// <summary>
/// T065 — <see cref="ProcessingNotifier"/> targets only the owning user's SignalR group
/// (<see cref="DocumentProcessingHub.UserGroup"/>), never a broadcast — the other half of the
/// group-isolation guarantee alongside <see cref="DocumentProcessingHubTests"/>.
/// </summary>
public sealed class ProcessingNotifierTests
{
    private readonly IDocumentNotificationRepository _notificationRepository = Substitute.For<IDocumentNotificationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IHubClients _hubClients = Substitute.For<IHubClients>();
    private readonly IClientProxy _ownerGroupProxy = Substitute.For<IClientProxy>();

    private ProcessingNotifier CreateSut(string ownerUserId)
    {
        _hubClients.Group(DocumentProcessingHub.UserGroup(ownerUserId)).Returns(_ownerGroupProxy);
        var hubContext = Substitute.For<IHubContext<DocumentProcessingHub>>();
        hubContext.Clients.Returns(_hubClients);
        return new ProcessingNotifier(hubContext, _notificationRepository, _unitOfWork);
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
    public async Task NotifyAsync_ShouldPersistTheNotificationAndSendOnlyToTheOwningUsersGroup()
    {
        var sut = CreateSut("user-3");

        await sut.NotifyAsync("user-3", DocumentNotificationEventType.ProcessingCompleted, Guid.CreateVersion7(), "Done.", CancellationToken.None);

        _notificationRepository.Received(1).Add(Arg.Any<DocumentNotification>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _ownerGroupProxy.Received(1).SendCoreAsync("notificationCreated", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }
}
