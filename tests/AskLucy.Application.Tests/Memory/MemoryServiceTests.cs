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

/// <summary>tasks.md T036 — proves <see cref="MemoryService"/>'s three <see cref="MemoryRetrievalOutcomeType"/> branches and the composite ranking score (research.md Decisions 3/4), faking the embedding/vector-store/repository dependencies.</summary>
public sealed class MemoryServiceTests
{
    private readonly IMemoryPreferenceRepository _preferenceRepository = Substitute.For<IMemoryPreferenceRepository>();
    private readonly IEmbeddingProviderRepository _embeddingProviderRepository = Substitute.For<IEmbeddingProviderRepository>();
    private readonly IEmbeddingServiceResolver _embeddingServiceResolver = Substitute.For<IEmbeddingServiceResolver>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IMemoryVectorStore _vectorStore = Substitute.For<IMemoryVectorStore>();
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly MemoryService _service;
    private readonly EmbeddingProvider _provider;
    private const string UserId = "user-1";

    public MemoryServiceTests()
    {
        _provider = EmbeddingProvider.Create("openai", "text-embedding-3-small", 1536, EmbeddingHostingType.Cloud, isDefault: true, "test");
        _embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, Arg.Any<CancellationToken>()).Returns(_provider);
        _embeddingServiceResolver.Resolve("openai").Returns(_embeddingService);
        _embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EmbeddingResult(new float[1536], 1536));

        _service = new MemoryService(
            _preferenceRepository, _embeddingProviderRepository, _embeddingServiceResolver, _vectorStore,
            _memoryRepository, Substitute.For<ILogger<MemoryService>>());
    }

    /// <summary>Automatic mode + not sensitive → <c>CreateCandidate</c> already lands directly on <see cref="MemoryLifecycleState.Active"/> (see <c>Memory.cs</c>'s doc comment on the collapsed lifecycle) — no separate <c>Approve()</c> call needed or possible.</summary>
    private static MemoryEntity CreateActiveMemory(decimal importance, decimal confidence, string content = "The user prefers React.") =>
        MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.UserPreference, content, MemorySourceType.PassiveConversationAnalysis,
            null, importance, confidence, isSensitive: false, MemoryApprovalMode.Automatic, "test");

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ShouldReturnFound_WhenActiveCandidatesExist()
    {
        var memory = CreateActiveMemory(0.8m, 0.9m);
        _vectorStore.QueryNearestAsync(Arg.Any<float[]>(), UserId, null, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryVectorSearchCandidate> { new(memory.Id, 0.1) });
        _memoryRepository.GetActiveByIdsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Contains(memory.Id)), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryEntity> { memory });

        var outcome = await _service.RetrieveRelevantMemoriesAsync(UserId, Guid.NewGuid(), null, "What framework do I use?", CancellationToken.None);

        outcome.Type.Should().Be(MemoryRetrievalOutcomeType.Found);
        outcome.ContextText.Should().Contain("The user prefers React.");
        outcome.UsedMemories.Should().ContainSingle().Which.MemoryId.Should().Be(memory.Id);
    }

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ShouldRankHigherImportanceAndConfidenceFirst_WhenSimilarityIsEqual()
    {
        var weakMemory = CreateActiveMemory(0.2m, 0.2m, "Weak signal.");
        var strongMemory = CreateActiveMemory(0.9m, 0.9m, "Strong signal.");
        _vectorStore.QueryNearestAsync(Arg.Any<float[]>(), UserId, null, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryVectorSearchCandidate> { new(weakMemory.Id, 0.1), new(strongMemory.Id, 0.1) });
        _memoryRepository.GetActiveByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryEntity> { weakMemory, strongMemory });

        var outcome = await _service.RetrieveRelevantMemoriesAsync(UserId, Guid.NewGuid(), null, "query", CancellationToken.None);

        outcome.UsedMemories.Select(m => m.MemoryId).Should().StartWith(strongMemory.Id);
    }

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ShouldReturnNoneRelevant_WhenVectorStoreFindsNoCandidates()
    {
        _vectorStore.QueryNearestAsync(Arg.Any<float[]>(), UserId, null, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryVectorSearchCandidate>());

        var outcome = await _service.RetrieveRelevantMemoriesAsync(UserId, Guid.NewGuid(), null, "query", CancellationToken.None);

        outcome.Type.Should().Be(MemoryRetrievalOutcomeType.NoneRelevant);
        outcome.ContextText.Should().BeNull();
        outcome.UsedMemories.Should().BeEmpty();
    }

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ShouldReturnNoneRelevant_WhenMemoryIsDisabledForTheUser()
    {
        var preference = MemoryPreference.CreateDefault(UserId, "test");
        preference.SetMemoryEnabled(false, "test");
        _preferenceRepository.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(preference);

        var outcome = await _service.RetrieveRelevantMemoriesAsync(UserId, Guid.NewGuid(), null, "query", CancellationToken.None);

        outcome.Type.Should().Be(MemoryRetrievalOutcomeType.NoneRelevant);
        await _vectorStore.DidNotReceive().QueryNearestAsync(
            Arg.Any<float[]>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetrieveRelevantMemoriesAsync_ShouldReturnUnavailable_NeverThrow_WhenEmbeddingFails()
    {
        _embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<EmbeddingResult>>(_ => throw new InvalidOperationException("Embedding provider is down."));

        var outcome = await _service.RetrieveRelevantMemoriesAsync(UserId, Guid.NewGuid(), null, "query", CancellationToken.None);

        outcome.Type.Should().Be(MemoryRetrievalOutcomeType.Unavailable);
        outcome.UnavailableReason.Should().NotBeNullOrEmpty();
        outcome.UsedMemories.Should().BeEmpty();
    }
}
