using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Projects;

/// <summary>
/// tasks.md T078 (US5 AC1) — a project-scoped fact is available within the same Project's
/// conversations. The actual SQL-level "which rows match" scoping lives in
/// <c>SqlServerMemoryVectorStore</c> (a raw-SQL <c>WHERE ProjectId = @projectId OR ProjectId IS
/// NULL</c> predicate, Persistence layer, proved against a real SQL Server instance per
/// constitution §10 — not runnable in this environment); this test proves the boundary this layer
/// owns: <c>MemoryService</c> passes the conversation's active <paramref>projectId</paramref>
/// through to that query unchanged, and surfaces whatever the store returns.
/// </summary>
public sealed class ProjectScopedMemoryTests
{
    private readonly IMemoryPreferenceRepository _preferenceRepository = Substitute.For<IMemoryPreferenceRepository>();
    private readonly IEmbeddingProviderRepository _embeddingProviderRepository = Substitute.For<IEmbeddingProviderRepository>();
    private readonly IEmbeddingServiceResolver _embeddingServiceResolver = Substitute.For<IEmbeddingServiceResolver>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IMemoryVectorStore _vectorStore = Substitute.For<IMemoryVectorStore>();
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly MemoryService _service;
    private const string UserId = "user-1";

    public ProjectScopedMemoryTests()
    {
        var provider = EmbeddingProvider.Create("openai", "text-embedding-3-small", 1536, EmbeddingHostingType.Cloud, true, "test");
        _embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, Arg.Any<CancellationToken>()).Returns(provider);
        _embeddingServiceResolver.Resolve("openai").Returns(_embeddingService);
        _embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new EmbeddingResult(new float[1536], 1536));
        _preferenceRepository.GetCategoryPreferencesAsync(UserId, Arg.Any<CancellationToken>()).Returns(new List<MemoryCategoryPreference>());

        _service = new MemoryService(
            _preferenceRepository, _embeddingProviderRepository, _embeddingServiceResolver, _vectorStore,
            _memoryRepository, Substitute.For<ILogger<MemoryService>>());
    }

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ShouldPassTheActiveProjectId_ThroughToTheVectorStoreQuery()
    {
        var projectId = Guid.NewGuid();
        var projectScopedMemory = MemoryEntity.CreateCandidate(
            UserId, projectId, MemoryCategory.ProjectContext, "Uses a custom color palette for this project",
            MemorySourceType.PassiveConversationAnalysis, null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");

        _vectorStore.QueryNearestAsync(Arg.Any<float[]>(), UserId, projectId, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryVectorSearchCandidate> { new(projectScopedMemory.Id, 0.1) });
        _memoryRepository.GetActiveByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryEntity> { projectScopedMemory });

        var outcome = await _service.RetrieveRelevantMemoriesAsync(UserId, Guid.NewGuid(), projectId, "What's the color scheme?", CancellationToken.None);

        outcome.Type.Should().Be(MemoryRetrievalOutcomeType.Found);
        outcome.UsedMemories.Should().ContainSingle().Which.MemoryId.Should().Be(projectScopedMemory.Id);
        await _vectorStore.Received(1).QueryNearestAsync(
            Arg.Any<float[]>(), UserId, projectId, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ShouldNeverQueryForAnotherProject_WhenADifferentProjectIsActive()
    {
        var activeProjectId = Guid.NewGuid();
        _vectorStore.QueryNearestAsync(Arg.Any<float[]>(), UserId, activeProjectId, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryVectorSearchCandidate>());

        await _service.RetrieveRelevantMemoriesAsync(UserId, Guid.NewGuid(), activeProjectId, "query", CancellationToken.None);

        var otherProjectId = Guid.NewGuid();
        await _vectorStore.DidNotReceive().QueryNearestAsync(
            Arg.Any<float[]>(), UserId, otherProjectId, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }
}
