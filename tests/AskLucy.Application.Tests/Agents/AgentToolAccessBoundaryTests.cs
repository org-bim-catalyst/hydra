using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Documents;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Documents;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Agents;

/// <summary>
/// spec.md SC-005/FR-049: an agent's effective access to Knowledge Bases, documents, memory, and
/// files is always the intersection of what it's configured for and what the executing user is
/// independently authorized for — never broader. Exercises all four read-scoped built-in tools
/// together, each with an owned + not-owned item in the same scenario, asserting the unauthorized
/// item is excluded/rejected — not just that the call itself succeeds.
/// </summary>
public sealed class AgentToolAccessBoundaryTests
{
    private static AgentToolExecutionContext Context(string userId = "user-1") =>
        new(Guid.NewGuid(), Guid.NewGuid(), userId, Guid.NewGuid(), Guid.NewGuid(), UserChatId: null);

    [Fact]
    public async Task KnowledgeSearchTool_ShouldExcludeAKnowledgeBaseTheCallerDoesNotOwn_FromTheEffectiveSearchSet()
    {
        var ownedId = Guid.NewGuid();
        var notOwnedId = Guid.NewGuid();
        var knowledgeBaseRepository = Substitute.For<IKnowledgeBaseRepository>();
        knowledgeBaseRepository.ResolveOwnedIdsAsync(
                "user-1", Arg.Is<IReadOnlyCollection<Guid>?>(c => c != null && c.Contains(ownedId) && c.Contains(notOwnedId)),
                Arg.Any<IReadOnlyCollection<Guid>?>(), Arg.Any<CancellationToken>())
            .Returns([ownedId]);
        var ragService = Substitute.For<IRagService>();
        ragService.RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Is<IReadOnlyList<Guid>>(ids => ids != null && ids.Count == 1 && ids[0] == ownedId), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.NoRelevantContent, null, [], null));

        var tool = new KnowledgeSearchTool(ragService, knowledgeBaseRepository);
        using var input = JsonDocument.Parse($$"""{"query":"policy","knowledgeBaseIds":["{{ownedId}}","{{notOwnedId}}"]}""");

        var result = await tool.ExecuteAsync(Context(), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await ragService.Received(1).RetrieveContextAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Is<IReadOnlyList<Guid>>(ids => ids != null && ids.Count == 1 && ids[0] == ownedId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DocumentSearchTool_ShouldScopeTheSearchToTheExecutingUser_NeverAnotherUsersDocuments()
    {
        var documentRepository = Substitute.For<IDocumentRepository>();
        documentRepository.SearchAsync("user-1", Arg.Any<DocumentListView>(), Arg.Any<Guid?>(), Arg.Any<DocumentSearchFilters>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(((IReadOnlyList<Document> Items, string? NextCursor))([], null));

        var tool = new DocumentSearchTool(documentRepository);
        using var input = JsonDocument.Parse("""{"query":"budget"}""");

        var result = await tool.ExecuteAsync(Context("user-1"), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        // The tool never accepts/forwards a caller-supplied owner id — context.UserId (from the
        // execution itself) is the only identity ever passed to the repository, so a search can
        // never be scoped to anyone but the executing user (FR-049).
        await documentRepository.Received(1).SearchAsync("user-1", Arg.Any<DocumentListView>(), Arg.Any<Guid?>(), Arg.Any<DocumentSearchFilters>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await documentRepository.DidNotReceive().SearchAsync(Arg.Is<string>(u => u != "user-1"), Arg.Any<DocumentListView>(), Arg.Any<Guid?>(), Arg.Any<DocumentSearchFilters>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MemorySearchTool_ShouldScopeRetrievalToTheExecutingUser_NeverAnotherUsersMemories()
    {
        var memoryService = Substitute.For<IMemoryService>();
        memoryService.RetrieveRelevantMemoriesAsync("user-1", Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));

        var tool = new MemorySearchTool(memoryService);
        using var input = JsonDocument.Parse("""{"query":"preferences"}""");

        var result = await tool.ExecuteAsync(Context("user-1"), input, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        await memoryService.Received(1).RetrieveRelevantMemoriesAsync("user-1", Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await memoryService.DidNotReceive().RetrieveRelevantMemoriesAsync(Arg.Is<string>(u => u != "user-1"), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FileReadTool_ShouldRejectADocumentTheCallerDoesNotOwn_WithA404NotA403()
    {
        var documentRepository = Substitute.For<IDocumentRepository>();
        var othersDocument = Document.Create(Guid.NewGuid(), "someone-else", "confidential.pdf", DocumentFileType.Pdf, 100, Guid.NewGuid(), "actor");
        documentRepository.GetByIdAsync(othersDocument.Id, Arg.Any<CancellationToken>()).Returns(othersDocument);

        var tool = new FileReadTool(documentRepository, Substitute.For<IFileStorage>());
        using var input = JsonDocument.Parse($$"""{"documentId":"{{othersDocument.Id}}"}""");

        var act = () => tool.ExecuteAsync(Context("user-1"), input, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
