using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Memory;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Memory;

/// <summary>tasks.md T092 (US6 AC2, FR-016, clarified 2026-08-09) — an ambiguous conflict flags the memory `PendingUserConfirmation` and raises a notification, without ever throwing/blocking (the live conversation turn that surfaced it is never interrupted — this service has no notion of "the turn" at all, it simply returns `false`, letting the caller's already-successful candidate-creation flow continue).</summary>
public sealed class AmbiguousConflictTests
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

    public AmbiguousConflictTests()
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

        var defaultProviderResolver = new DefaultProviderResolver(_aiProviderRepository, _aiModelRepository);

        _service = new MemoryConflictDetectionService(
            _vectorStore, _memoryRepository, _conflictRepository, _versionRepository, _auditLogRepository, _notifier,
            _embeddingProviderRepository, _embeddingServiceResolver, _aiProviderRepository, _aiModelRepository,
            _aiProviderResolver, defaultProviderResolver, Substitute.For<ILogger<MemoryConflictDetectionService>>());
    }

    [Fact]
    public async Task DetectAndResolveAsync_ShouldFlagPendingUserConfirmation_AndNotify_WithoutThrowingOrDeletingEither_OnAmbiguousConflict()
    {
        var existingMemory = MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.PersonalFact, "Works on residential projects", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");
        var candidateMemory = MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.PersonalFact, "Also works on commercial projects", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");

        _vectorStore.QueryNearestAsync(Arg.Any<float[]>(), UserId, null, Arg.Any<int>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryVectorSearchCandidate> { new(existingMemory.Id, 0.2) });
        _memoryRepository.GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryEntity> { existingMemory });

        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult(
                $$"""[{"memoryId":"{{existingMemory.Id}}","verdict":"AmbiguousSupersedeOrSupplement"}]""",
                new ChatUsage(null, null, null, null, null)));

        var act = () => _service.DetectAndResolveAsync(candidateMemory, CancellationToken.None);
        var consumed = await act.Should().NotThrowAsync();
        consumed.Which.Should().BeFalse("neither memory is consumed for an ambiguous conflict — both remain, pending resolution");

        existingMemory.IsDeleted.Should().BeFalse();
        candidateMemory.IsDeleted.Should().BeFalse();
        _conflictRepository.Received(1).Add(Arg.Is<MemoryConflict>(c =>
            c.ExistingMemoryId == existingMemory.Id && c.NewMemoryId == candidateMemory.Id
            && c.ResolutionStatus == MemoryConflictResolutionStatus.PendingUserConfirmation));
        _auditLogRepository.Received(1).Add(Arg.Is<MemoryAuditLog>(a => a.Action == MemoryAuditAction.ConflictDetected));
        await _notifier.Received(1).NotifyAsync(
            existingMemory.UserId, candidateMemory.Id, MemoryNotificationEventType.ConflictNeedsConfirmation, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
