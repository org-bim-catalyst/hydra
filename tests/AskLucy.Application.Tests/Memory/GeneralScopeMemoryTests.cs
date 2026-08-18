using AskLucy.Application.Abstractions;
using AskLucy.Application.Memory;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Memory;

/// <summary>
/// tasks.md T079 (US5 AC2) — a conversation with no Project only considers general
/// (non-project-scoped) memories. Mirrors <c>ProjectScopedMemoryTests</c>'s reasoning: the
/// SQL-level "ProjectId IS NULL only" predicate lives in <c>SqlServerMemoryVectorStore</c>
/// (Persistence layer, real-DB-only per constitution §10); this proves <c>MemoryService</c> passes
/// <c>null</c> through for a general (unscoped) conversation.
/// </summary>
public sealed class GeneralScopeMemoryTests
{
    private readonly IMemoryPreferenceRepository _preferenceRepository = Substitute.For<IMemoryPreferenceRepository>();
    private readonly IEmbeddingProviderRepository _embeddingProviderRepository = Substitute.For<IEmbeddingProviderRepository>();
    private readonly IEmbeddingServiceResolver _embeddingServiceResolver = Substitute.For<IEmbeddingServiceResolver>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IMemoryVectorStore _vectorStore = Substitute.For<IMemoryVectorStore>();
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly MemoryService _service;
    private const string UserId = "user-1";

    public GeneralScopeMemoryTests()
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
    public async Task RetrieveRelevantMemoriesAsync_ShouldQueryWithNullProjectId_WhenTheConversationHasNoProject()
    {
        var generalMemory = MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.UserPreference, "Prefers concise answers", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");

        _vectorStore.QueryNearestAsync(Arg.Any<float[]>(), UserId, null, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryVectorSearchCandidate> { new(generalMemory.Id, 0.1) });
        _memoryRepository.GetActiveByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryEntity> { generalMemory });

        var outcome = await _service.RetrieveRelevantMemoriesAsync(UserId, Guid.NewGuid(), projectId: null, "query", CancellationToken.None);

        outcome.Type.Should().Be(MemoryRetrievalOutcomeType.Found);
        await _vectorStore.Received(1).QueryNearestAsync(
            Arg.Any<float[]>(), UserId, null, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }
}
