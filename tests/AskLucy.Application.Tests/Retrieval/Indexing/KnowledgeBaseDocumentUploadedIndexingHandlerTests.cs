using Hangfire.States;
using AskLucy.Application.Abstractions;
using AskLucy.Application.OperationalFailures.Abstractions;
using AskLucy.Application.Retrieval.Indexing;
using AskLucy.Application.Workflows.EventTriggers;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AskLucy.Application.Tests.Retrieval.Indexing;

/// <summary>T235 — <see cref="KnowledgeBaseDocumentUploadedIndexingHandler"/>.</summary>
public sealed class KnowledgeBaseDocumentUploadedIndexingHandlerTests
{
    private readonly IKnowledgeBaseDocumentRepository _knowledgeBaseDocumentRepository = Substitute.For<IKnowledgeBaseDocumentRepository>();
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
    private readonly IIndexingJobRepository _indexingJobRepository = Substitute.For<IIndexingJobRepository>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly IOperationalFailureRecorder _operationalFailureRecorder = Substitute.For<IOperationalFailureRecorder>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private (KnowledgeBase KnowledgeBase, KnowledgeBaseDocument Document) SetUp(KnowledgeBaseDocumentProcessingStatus status = KnowledgeBaseDocumentProcessingStatus.Ready)
    {
        var knowledgeBase = KnowledgeBase.Create("Construction Standards", "user-1", "user-1");
        var document = KnowledgeBaseDocument.Create(knowledgeBase.Id, null, "spec.md", "stored.md", "text/markdown", 1024, null, "user-1");
        if (status == KnowledgeBaseDocumentProcessingStatus.Failed)
        {
            document.MarkProcessingFailed("user-1");
        }

        _knowledgeBaseDocumentRepository.GetByIdAsync(document.Id, Arg.Any<CancellationToken>()).Returns(document);
        _knowledgeBaseRepository.GetByIdAsync(knowledgeBase.Id, Arg.Any<CancellationToken>()).Returns(knowledgeBase);
        _backgroundJobClient.Create(Arg.Any<Hangfire.Common.Job>(), Arg.Any<IState>()).Returns("hangfire-job-1");

        return (knowledgeBase, document);
    }

    private KnowledgeBaseDocumentUploadedIndexingHandler CreateSut() => new(
        _knowledgeBaseDocumentRepository, _knowledgeBaseRepository, _indexingJobRepository, _backgroundJobClient,
        _operationalFailureRecorder, _unitOfWork, Substitute.For<ILogger<KnowledgeBaseDocumentUploadedIndexingHandler>>());

    [Fact]
    public async Task Handle_ShouldCreateJobMarkIndexingAndEnqueue_WhenDocumentReady()
    {
        var (knowledgeBase, document) = SetUp();

        await CreateSut().Handle(new DocumentUploadedNotification(document.Id, knowledgeBase.Id, "user-1", document.FileName), CancellationToken.None);

        knowledgeBase.IndexStatus.Should().Be(KnowledgeBaseIndexStatus.Indexing);
        _indexingJobRepository.Received(1).Add(Arg.Is<IndexingJob>(j =>
            j.KnowledgeBaseId == knowledgeBase.Id && j.KnowledgeBaseDocumentId == document.Id && j.Status == IndexingJobStatus.Queued));
        _backgroundJobClient.Received(1).Create(Arg.Any<Hangfire.Common.Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task Handle_ShouldSkip_WhenDocumentProcessingAlreadyFailed()
    {
        var (knowledgeBase, document) = SetUp(KnowledgeBaseDocumentProcessingStatus.Failed);

        await CreateSut().Handle(new DocumentUploadedNotification(document.Id, knowledgeBase.Id, "user-1", document.FileName), CancellationToken.None);

        knowledgeBase.IndexStatus.Should().Be(KnowledgeBaseIndexStatus.NotIndexed);
        _indexingJobRepository.DidNotReceiveWithAnyArgs().Add(default!);
        _backgroundJobClient.DidNotReceiveWithAnyArgs().Create(default!, default!);
    }

    [Fact]
    public async Task Handle_ShouldFailJobRecordOperationalFailureAndRethrow_WhenEnqueueThrows()
    {
        var (knowledgeBase, document) = SetUp();
        _backgroundJobClient.Create(Arg.Any<Hangfire.Common.Job>(), Arg.Any<IState>()).Throws(new InvalidOperationException("Hangfire storage unavailable"));

        var act = () => CreateSut().Handle(new DocumentUploadedNotification(document.Id, knowledgeBase.Id, "user-1", document.FileName), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _indexingJobRepository.Received(1).Add(Arg.Is<IndexingJob>(j => j.Status == IndexingJobStatus.Failed));
        _operationalFailureRecorder.Received(1).Record(Arg.Any<OperationalFailureReport>());
    }
}
