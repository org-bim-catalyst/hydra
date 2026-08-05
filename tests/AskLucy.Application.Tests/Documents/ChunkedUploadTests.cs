using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Commands.StartUpload;
using AskLucy.Application.Documents.Commands.UploadChunk;
using AskLucy.Application.Options;
using AskLucy.Domain.Common;
using AskLucy.Domain.Documents;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Documents;

public sealed class ChunkedUploadTests
{
    private readonly IDocumentUploadSessionRepository _sessionRepository = Substitute.For<IDocumentUploadSessionRepository>();
    private readonly IResumableUploadStorage _resumableStorage = Substitute.For<IResumableUploadStorage>();
    private readonly IDocumentRepository _documentRepository = Substitute.For<IDocumentRepository>();
    private readonly IDocumentStatisticsRepository _statisticsRepository = Substitute.For<IDocumentStatisticsRepository>();
    private readonly IProcessingNotifier _processingNotifier = Substitute.For<IProcessingNotifier>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();

    public ChunkedUploadTests()
    {
        _statisticsRepository.ComputeAggregateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new DocumentStatisticsAggregate(0, 0, null, "{}", "{}"));
    }

    private static IOptions<DocumentUploadOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new DocumentUploadOptions { ChunkSizeBytes = 256, MaxFileSizeBytes = 10_000 });

    private static IOptions<DocumentStorageQuotaOptions> QuotaOptions() =>
        Microsoft.Extensions.Options.Options.Create(new DocumentStorageQuotaOptions());

    [Fact]
    public async Task StartUpload_ShouldCreateSessionAndReturnChunkSize()
    {
        _currentUser.UserId.Returns("user-1");
        var handler = new StartUploadCommandHandler(
            _sessionRepository, _documentRepository, _statisticsRepository, _processingNotifier, Options(), QuotaOptions(), _unitOfWork, _currentUser);

        var result = await handler.Handle(new StartUploadCommand("report.pdf", 512), CancellationToken.None);

        result.ChunkSizeBytes.Should().Be(256);
        _sessionRepository.Received(1).Add(Arg.Is<DocumentUploadSession>(s => s.OwnerId == "user-1" && s.FileName == "report.pdf"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartUpload_ShouldReject_WhenDeclaredSizeExceedsMax()
    {
        _currentUser.UserId.Returns("user-1");
        var handler = new StartUploadCommandHandler(
            _sessionRepository, _documentRepository, _statisticsRepository, _processingNotifier, Options(), QuotaOptions(), _unitOfWork, _currentUser);

        var act = () => handler.Handle(new StartUploadCommand("huge.pdf", 20_000), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task UploadChunk_ShouldAppendAndReturnNextExpectedIndex()
    {
        _currentUser.UserId.Returns("user-1");
        var session = DocumentUploadSession.Create("user-1", "report.pdf", 512, 256, DateTime.UtcNow.AddHours(1), "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(0L);

        var handler = new UploadChunkCommandHandler(_sessionRepository, _resumableStorage, _currentUser);
        using var chunk = new MemoryStream(Encoding.UTF8.GetBytes(new string('a', 256)));

        var result = await handler.Handle(new UploadChunkCommand(session.Id, 0, chunk), CancellationToken.None);

        result.ReceivedChunkIndex.Should().Be(0);
        result.NextExpectedChunkIndex.Should().Be(1);
        await _resumableStorage.Received(1).AppendChunkAsync(session.Id.ToString(), chunk, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadChunk_ShouldDeriveNextExpectedIndexFromActualStoredSize_NotAStoredCounter()
    {
        // Resume scenario: the client lost track of progress and re-asks; the handler must
        // derive the next index from what's ACTUALLY on disk (data-model.md), not a separate
        // counter that could have drifted.
        _currentUser.UserId.Returns("user-1");
        var session = DocumentUploadSession.Create("user-1", "report.pdf", 1024, 256, DateTime.UtcNow.AddHours(1), "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(512L); // 2 chunks already received

        var handler = new UploadChunkCommandHandler(_sessionRepository, _resumableStorage, _currentUser);
        using var chunk = new MemoryStream(Encoding.UTF8.GetBytes(new string('b', 256)));

        var result = await handler.Handle(new UploadChunkCommand(session.Id, 2, chunk), CancellationToken.None);

        result.ReceivedChunkIndex.Should().Be(2);
        result.NextExpectedChunkIndex.Should().Be(3);
    }

    [Fact]
    public async Task UploadChunk_ShouldReject_WhenChunkIndexIsOutOfOrder()
    {
        _currentUser.UserId.Returns("user-1");
        var session = DocumentUploadSession.Create("user-1", "report.pdf", 1024, 256, DateTime.UtcNow.AddHours(1), "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(0L);

        var handler = new UploadChunkCommandHandler(_sessionRepository, _resumableStorage, _currentUser);
        using var chunk = new MemoryStream([1, 2, 3]);

        var act = () => handler.Handle(new UploadChunkCommand(session.Id, 5, chunk), CancellationToken.None);

        await act.Should().ThrowAsync<DomainRuleViolationException>();
    }

    [Fact]
    public async Task UploadChunk_ShouldReject_OnceAccumulatedSizeWouldExceedTheSessionsDeclaredSize()
    {
        // constitution §8 — without this, a client could keep appending well-formed, in-order
        // chunks forever without ever calling Complete, consuming unbounded temp storage; the
        // declared-size check at Complete time only catches the mismatch after the fact.
        _currentUser.UserId.Returns("user-1");
        var session = DocumentUploadSession.Create("user-1", "report.pdf", 1024, 256, DateTime.UtcNow.AddHours(1), "user-1"); // 4 valid chunks: 0-3.
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        _resumableStorage.GetSizeAsync(session.Id.ToString(), Arg.Any<CancellationToken>()).Returns(1024L); // Already at the declared size.

        var handler = new UploadChunkCommandHandler(_sessionRepository, _resumableStorage, _currentUser);
        using var chunk = new MemoryStream(Encoding.UTF8.GetBytes(new string('c', 256)));

        var act = () => handler.Handle(new UploadChunkCommand(session.Id, 4, chunk), CancellationToken.None); // In order, but past the ceiling.

        await act.Should().ThrowAsync<DomainRuleViolationException>();
        await _resumableStorage.DidNotReceive().AppendChunkAsync(session.Id.ToString(), Arg.Any<Stream>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadChunk_ShouldThrowNotFound_WhenSessionOwnedByAnotherUser()
    {
        _currentUser.UserId.Returns("user-2");
        var session = DocumentUploadSession.Create("user-1", "report.pdf", 1024, 256, DateTime.UtcNow.AddHours(1), "user-1");
        _sessionRepository.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

        var handler = new UploadChunkCommandHandler(_sessionRepository, _resumableStorage, _currentUser);
        using var chunk = new MemoryStream([1, 2, 3]);

        var act = () => handler.Handle(new UploadChunkCommand(session.Id, 0, chunk), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
