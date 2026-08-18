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

/// <summary>tasks.md T071 (US4 AC4, FR-025) — disabling one category stops new/used memories in that category only; other categories keep working.</summary>
public sealed class MemoryCategoryDisableTests
{
    private readonly IMemoryPreferenceRepository _preferenceRepository = Substitute.For<IMemoryPreferenceRepository>();
    private readonly IEmbeddingProviderRepository _embeddingProviderRepository = Substitute.For<IEmbeddingProviderRepository>();
    private readonly IEmbeddingServiceResolver _embeddingServiceResolver = Substitute.For<IEmbeddingServiceResolver>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IMemoryVectorStore _vectorStore = Substitute.For<IMemoryVectorStore>();
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly MemoryService _service;
    private const string UserId = "user-1";

    public MemoryCategoryDisableTests()
    {
        var provider = EmbeddingProvider.Create("openai", "text-embedding-3-small", 1536, EmbeddingHostingType.Cloud, true, "test");
        _embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, Arg.Any<CancellationToken>()).Returns(provider);
        _embeddingServiceResolver.Resolve("openai").Returns(_embeddingService);
        _embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new EmbeddingResult(new float[1536], 1536));

        _service = new MemoryService(
            _preferenceRepository, _embeddingProviderRepository, _embeddingServiceResolver, _vectorStore,
            _memoryRepository, Substitute.For<ILogger<MemoryService>>());
    }

    private static MemoryEntity CreateActive(MemoryCategory category, string content) =>
        MemoryEntity.CreateCandidate(
            UserId, null, category, content, MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ShouldExcludeOnlyTheDisabledCategory_KeepingOthersUsable()
    {
        var disabledCategoryMemory = CreateActive(MemoryCategory.ProjectContext, "Disabled category content");
        var enabledCategoryMemory = CreateActive(MemoryCategory.PersonalFact, "Enabled category content");

        var disabledPreference = MemoryCategoryPreference.CreateDefault(UserId, MemoryCategory.ProjectContext, "test");
        disabledPreference.Update(null, isEnabled: false, "test");
        var enabledPreference = MemoryCategoryPreference.CreateDefault(UserId, MemoryCategory.PersonalFact, "test");
        _preferenceRepository.GetCategoryPreferencesAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new List<MemoryCategoryPreference> { disabledPreference, enabledPreference });

        _vectorStore.QueryNearestAsync(Arg.Any<float[]>(), UserId, null, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryVectorSearchCandidate>
            {
                new(disabledCategoryMemory.Id, 0.1),
                new(enabledCategoryMemory.Id, 0.1),
            });
        _memoryRepository.GetActiveByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryEntity> { disabledCategoryMemory, enabledCategoryMemory });

        var outcome = await _service.RetrieveRelevantMemoriesAsync(UserId, Guid.NewGuid(), null, "query", CancellationToken.None);

        outcome.Type.Should().Be(MemoryRetrievalOutcomeType.Found);
        outcome.UsedMemories.Should().ContainSingle().Which.MemoryId.Should().Be(enabledCategoryMemory.Id);
    }
}
