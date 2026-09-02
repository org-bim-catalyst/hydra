using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Domain.Agents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

public sealed class KnowledgeSearchToolTests
{
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IKnowledgeBaseRepository _knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();

    private static AgentToolExecutionContext Context() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "user-1", Guid.NewGuid(), Guid.NewGuid(), UserChatId: null);

    [Fact]
    public async Task ExecuteAsync_ShouldPreserveCitations_WhenTheRetrievalIsGrounded()
    {
        var kbId = Guid.NewGuid();
        _knowledgeBaseRepository.ResolveOwnedIdsAsync("user-1", Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns([kbId]);

        var citation = new RagCitationContext(Guid.NewGuid(), kbId, Guid.NewGuid(), Guid.NewGuid(), "Onboarding Guide", "HR Docs", 3, "Getting Started", "New hires should...");
        _ragService.RetrieveContextAsync(Arg.Any<Guid>(), "onboarding", Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "New hires should complete orientation.", [citation], null));

        var tool = new KnowledgeSearchTool(_ragService, _knowledgeBaseRepository);
        using var input = JsonDocument.Parse("""{"query":"onboarding"}""");

        var result = await tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Output!.RootElement.GetProperty("contextText").GetString().Should().Be("New hires should complete orientation.");
        var citations = result.Output.RootElement.GetProperty("citations").EnumerateArray().ToList();
        citations.Should().ContainSingle();
        citations[0].GetProperty("documentTitle").GetString().Should().Be("Onboarding Guide");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNeverSearchBeyondTheCallersAuthorizedKnowledgeBases()
    {
        var ownedId = Guid.NewGuid();
        var notOwnedId = Guid.NewGuid();

        // ResolveOwnedIdsAsync (the real repository's own contract) silently excludes ids the
        // caller doesn't own — this test asserts the tool passes candidateIds through rather
        // than bypassing that resolution (FR-049).
        _knowledgeBaseRepository.ResolveOwnedIdsAsync("user-1", Arg.Is<IReadOnlyCollection<Guid>?>(c => c != null && c.Contains(ownedId) && c.Contains(notOwnedId)), Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns([ownedId]);
        _ragService.RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Is<IReadOnlyList<Guid>>(ids => ids != null && ids.Count == 1 && ids[0] == ownedId), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.NoRelevantContent, null, [], null));

        var tool = new KnowledgeSearchTool(_ragService, _knowledgeBaseRepository);
        using var input = JsonDocument.Parse($$"""{"query":"onboarding","knowledgeBaseIds":["{{ownedId}}","{{notOwnedId}}"]}""");

        var result = await tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await _ragService.Received(1).RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Is<IReadOnlyList<Guid>>(ids => ids != null && ids.Count == 1 && ids[0] == ownedId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFail_WhenQueryIsMissing()
    {
        var tool = new KnowledgeSearchTool(_ragService, _knowledgeBaseRepository);
        using var input = JsonDocument.Parse("{}");

        var result = await tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }
}
