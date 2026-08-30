using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Memory;
using AskLucy.Application.Tests.Ai;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Memory;

/// <summary>tasks.md T091 (US6 AC1, FR-015) — a direct contradiction auto-updates the existing memory in place, preserves the prior value as history, and never surfaces anything to the live conversation turn (the caller decides that; this service just resolves synchronously and returns).</summary>
public sealed class DirectContradictionTests
{
    private readonly IMemoryVectorStore _vectorStore = Substitute.For<IMemoryVectorStore>();
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly IMemoryConflictRepository _conflictRepository = Substitute.For<IMemoryConflictRepository>();
    private readonly IMemoryVersionRepository _versionRepository = Substitute.For<IMemoryVersionRepository>();
    private readonly IMemoryAuditLogRepository _auditLogRepository = Substitute.For<IMemoryAuditLogRepository>();
    private readonly IMemoryNotifier _notifier = Substitute.For<IMemoryNotifier>();
    private readonly IEmbeddingProviderRepository _embeddingProviderRepository = Substitute.For<IEmbeddingProviderRepository>();
    private readonly IEmbeddingServiceResolver _embeddingServiceResolver = Substitute.For<IEmbeddingServiceResolver>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IAIProviderRepository _aiProviderRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _aiModelRepository = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _aiProviderResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly MemoryConflictDetectionService _service;
    private const string UserId = "user-1";

    public DirectContradictionTests()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "test");
        provider.SetCredential("ciphertext", "test");
        provider.Enable("test");
        var model = AIModel.Create(
            provider.Id, "gpt-4.1", "GPT-4.1", 128000, 16384,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");

        _aiProviderRepository.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { provider });
        _aiModelRepository.ListAvailableByProviderIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(new List<AIModel> { model });
        _aiProviderRepository.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _aiModelRepository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        _aiProviderResolver.Resolve("openai").Returns(_aiProvider);

        var embeddingProvider = EmbeddingProvider.Create("openai", "text-embedding-3-small", 1536, EmbeddingHostingType.Cloud, true, "test");
        _embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, Arg.Any<CancellationToken>()).Returns(embeddingProvider);
        _embeddingServiceResolver.Resolve("openai").Returns(_embeddingService);
        _embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new EmbeddingResult(new float[1536], 1536));

        var defaultProviderResolver = CapabilityResolverTestFactory.Unassigned(_aiProviderRepository, _aiModelRepository);

        _service = new MemoryConflictDetectionService(
            _vectorStore, _memoryRepository, _conflictRepository, _versionRepository, _auditLogRepository, _notifier,
            _embeddingProviderRepository, _embeddingServiceResolver, _aiProviderRepository, _aiModelRepository,
            _aiProviderResolver, defaultProviderResolver, Substitute.For<ILogger<MemoryConflictDetectionService>>());
    }

    [Fact]
    public async Task DetectAndResolveAsync_ShouldUpdateTheExistingMemoryInPlace_AndPreserveThePriorValueAsHistory_OnDirectContradiction()
    {
        var existingMemory = MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.UserPreference, "I use Angular", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");
        var candidateMemory = MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.UserPreference, "I moved to React", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");

        _vectorStore.QueryNearestAsync(Arg.Any<float[]>(), UserId, null, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryVectorSearchCandidate> { new(existingMemory.Id, 0.05) });
        _memoryRepository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryEntity> { existingMemory });

        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult(
                $$"""[{"memoryId":"{{existingMemory.Id}}","verdict":"DirectContradiction"}]""",
                new ChatUsage(null, null, null, null, null)));

        var consumed = await _service.DetectAndResolveAsync(candidateMemory, CancellationToken.None);

        consumed.Should().BeTrue();
        existingMemory.Content.Should().Be("I moved to React");
        candidateMemory.IsDeleted.Should().BeTrue("the candidate is merged into the existing memory, not kept as a separate row");
        _versionRepository.Received(1).Add(Arg.Is<MemoryVersion>(v =>
            v != null && v.MemoryId == existingMemory.Id && v.PreviousContent == "I use Angular" && v.ChangeReason == MemoryChangeReason.ConflictResolutionSupersede));
        _conflictRepository.Received(1).Add(Arg.Is<MemoryConflict>(c =>
            c != null && c.ExistingMemoryId == existingMemory.Id && c.ResolutionStatus == MemoryConflictResolutionStatus.AutoResolved));
        _auditLogRepository.Received(1).Add(Arg.Is<MemoryAuditLog>(a => a != null && a.Action == MemoryAuditAction.ConflictDetected));
        _auditLogRepository.Received(1).Add(Arg.Is<MemoryAuditLog>(a => a != null && a.Action == MemoryAuditAction.ConflictResolved));
        await _notifier.DidNotReceive().NotifyAsync(
            Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<MemoryNotificationEventType>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
