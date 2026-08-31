using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.CancelUpload;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Documents;

public sealed class CancelUploadTests
{
    private readonly IDocumentUploadSessionRepository _sessionRepository = Substitute.For<IDocumentUploadSessionRepository>();
    private readonly IResumableUploadStorage _resumableStorage = Substitute.For<IResumableUploadStorage>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    [Fact]
    public async Task CancelUpload_ShouldMarkSessionCancelled_AndDeleteStagedChunks()
    {
        _currentUser.UserId.Returns("user-1");
        var session = DocumentUploadSession.Create("user-1", "report.pdf", 1024, 256, DateTime.UtcNow.AddHours(1), "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new CancelUploadCommandHandler(_sessionRepository, _resumableStorage, _unitOfWork, _currentUser);
        await handler.Handle(new CancelUploadCommand(session.Id), CancellationToken.None);

        session.Status.Should().Be(DocumentUploadSessionStatus.Cancelled);
        await _resumableStorage.Received(1).DeleteAsync(session.Id.ToString(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelUpload_ShouldThrowNotFound_WhenCallerDoesNotOwnSession()
    {
        _currentUser.UserId.Returns("user-2");
        var session = DocumentUploadSession.Create("user-1", "report.pdf", 1024, 256, DateTime.UtcNow.AddHours(1), "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new CancelUploadCommandHandler(_sessionRepository, _resumableStorage, _unitOfWork, _currentUser);
        var act = () => handler.Handle(new CancelUploadCommand(session.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _resumableStorage.DidNotReceiveWithAnyArgs().DeleteAsync(default!, TestContext.Current.CancellationToken);
    }
}
