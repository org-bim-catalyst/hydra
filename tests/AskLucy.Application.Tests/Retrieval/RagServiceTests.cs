using AskLucy.Application.Abstractions;
using AskLucy.Application.Retrieval;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Retrieval;

/// <summary>tasks.md T046 — proves <see cref="RagService"/>'s three <see cref="RagRetrievalOutcomeType"/> branches, faking the search pipeline (<see cref="IMediator"/>) rather than a real embedding/vector-store dependency.</summary>
public sealed class RagServiceTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly RagService _service;

    public RagServiceTests() => _service = new RagService(_mediator, Substitute.For<ILogger<RagService>>());

    [Fact]
    public async Task RetrieveContextAsync_ShouldReturnGrounded_WhenResultsAreFound()
    {
        var chunkId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var results = new List<SearchResultItemDto>
        {
            new(chunkId, Guid.NewGuid(), Guid.NewGuid(), kbId, "Doc.pdf", "KB", 1, "Intro", "Some relevant excerpt.", 0.9m, 0.9m, null, null, 1),
        };
        _mediator.Send(Arg.Any<IRequest<IReadOnlyList<SearchResultItemDto>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SearchResultItemDto>>(results));

        var outcome = await _service.RetrieveContextAsync(Guid.NewGuid(), "What is this about?", [kbId], CancellationToken.None);

        outcome.Type.Should().Be(RagRetrievalOutcomeType.Grounded);
        outcome.ContextText.Should().Contain("Some relevant excerpt.");
        outcome.Citations.Should().ContainSingle().Which.DocumentChunkId.Should().Be(chunkId);
        outcome.UnavailableReason.Should().BeNull();
    }

    [Fact]
    public async Task RetrieveContextAsync_ShouldReturnNoRelevantContent_WhenNoResultsFound()
    {
        _mediator.Send(Arg.Any<IRequest<IReadOnlyList<SearchResultItemDto>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SearchResultItemDto>>([]));

        var outcome = await _service.RetrieveContextAsync(Guid.NewGuid(), "query", [Guid.NewGuid()], CancellationToken.None);

        outcome.Type.Should().Be(RagRetrievalOutcomeType.NoRelevantContent);
        outcome.Citations.Should().BeEmpty();
        outcome.ContextText.Should().BeNull();
    }

    [Fact]
    public async Task RetrieveContextAsync_ShouldReturnNoRelevantContent_WhenNoKnowledgeBasesAreAttached()
    {
        var outcome = await _service.RetrieveContextAsync(Guid.NewGuid(), "query", [], CancellationToken.None);

        outcome.Type.Should().Be(RagRetrievalOutcomeType.NoRelevantContent);
        await _mediator.DidNotReceive().Send(Arg.Any<IRequest<IReadOnlyList<SearchResultItemDto>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetrieveContextAsync_ShouldReturnUnavailable_NeverThrow_WhenSearchPipelineFails()
    {
        _mediator.Send(Arg.Any<IRequest<IReadOnlyList<SearchResultItemDto>>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<SearchResultItemDto>>>(_ => throw new InvalidOperationException("Vector store is down."));

        var outcome = await _service.RetrieveContextAsync(Guid.NewGuid(), "query", [Guid.NewGuid()], CancellationToken.None);

        outcome.Type.Should().Be(RagRetrievalOutcomeType.Unavailable);
        outcome.UnavailableReason.Should().NotBeNullOrEmpty();
        outcome.Citations.Should().BeEmpty();
    }

    [Fact]
    public async Task RetrieveContextAsync_ShouldTrimLowerRankedChunks_WhenContextWouldExceedTheTokenBudget()
    {
        var kbId = Guid.NewGuid();
        // Each excerpt is ~16000 chars (~4000 estimated tokens via the text.Length/4 heuristic) —
        // the system default budget is 4000 tokens, so only the first (best-ranked) result should
        // survive into the context/citations; the second, lower-ranked one must be trimmed (FR-024).
        var bigExcerpt = new string('a', 16000);
        var results = new List<SearchResultItemDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), kbId, "Doc1.pdf", "KB", 1, null, bigExcerpt, 0.95m, 0.95m, null, null, 1),
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), kbId, "Doc2.pdf", "KB", 1, null, bigExcerpt, 0.5m, 0.5m, null, null, 2),
        };
        _mediator.Send(Arg.Any<IRequest<IReadOnlyList<SearchResultItemDto>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<SearchResultItemDto>>(results));

        var outcome = await _service.RetrieveContextAsync(Guid.NewGuid(), "query", [kbId], CancellationToken.None);

        outcome.Type.Should().Be(RagRetrievalOutcomeType.Grounded);
        outcome.Citations.Should().ContainSingle().Which.DocumentTitle.Should().Be("Doc1.pdf");
    }
}
