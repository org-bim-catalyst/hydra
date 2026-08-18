using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Memory;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Memory;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Memory;

/// <summary>tasks.md T038 (US1 AC2, FR-022) — with memory disabled for the user, extraction never creates a candidate for a fact stated during the disabled period.</summary>
public sealed class MemoryDisabledExclusionTests
{
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly IMessageRepository _messageRepository = Substitute.For<IMessageRepository>();
    private readonly IMemoryRepository _memoryRepository = Substitute.For<IMemoryRepository>();
    private readonly IMemoryPreferenceRepository _preferenceRepository = Substitute.For<IMemoryPreferenceRepository>();
    private readonly IMemoryApprovalRepository _approvalRepository = Substitute.For<IMemoryApprovalRepository>();
    private readonly IMemoryAuditLogRepository _auditLogRepository = Substitute.For<IMemoryAuditLogRepository>();
    private readonly IMemoryEmbeddingRepository _memoryEmbeddingRepository = Substitute.For<IMemoryEmbeddingRepository>();
    private readonly IMemoryConflictDetectionService _conflictDetectionService = Substitute.For<IMemoryConflictDetectionService>();
    private readonly IMemoryNotifier _notifier = Substitute.For<IMemoryNotifier>();
    private readonly IEmbeddingProviderRepository _embeddingProviderRepository = Substitute.For<IEmbeddingProviderRepository>();
    private readonly IEmbeddingServiceResolver _embeddingServiceResolver = Substitute.For<IEmbeddingServiceResolver>();
    private readonly IMemoryVectorStore _vectorStore = Substitute.For<IMemoryVectorStore>();
    private readonly IAIProviderRepository _aiProviderRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _aiModelRepository = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _aiProviderResolver = Substitute.For<IAIProviderResolver>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly MemoryExtractionJob _job;

    public MemoryDisabledExclusionTests()
    {
        // DefaultProviderResolver is a concrete Application class, not an interface — faking its
        // own two repository dependencies rather than the class itself keeps this test from ever
        // reaching it, since a disabled-memory user chat returns before any provider resolution.
        var defaultProviderResolver = new DefaultProviderResolver(_aiProviderRepository, _aiModelRepository);

        _job = new MemoryExtractionJob(
            _userChatRepository, _messageRepository, _memoryRepository, _preferenceRepository, _approvalRepository,
            _auditLogRepository, _memoryEmbeddingRepository, _conflictDetectionService, _notifier,
            _embeddingProviderRepository, _embeddingServiceResolver,
            _vectorStore, _aiProviderRepository, _aiModelRepository, _aiProviderResolver, defaultProviderResolver,
            _unitOfWork, Substitute.For<ILogger<MemoryExtractionJob>>());
    }

    [Fact]
    public async Task RunAsync_ShouldNeverCreateAMemory_WhenMemoryIsDisabledForTheUser()
    {
        var chat = UserChat.Create("Test chat", "user-1", null, "user-1");
        _userChatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);

        var preference = MemoryPreference.CreateDefault("user-1", "test");
        preference.SetMemoryEnabled(false, "test");
        _preferenceRepository.GetByUserIdAsync("user-1", Arg.Any<CancellationToken>()).Returns(preference);

        await _job.RunAsync(chat.Id, CancellationToken.None);

        await _messageRepository.DidNotReceive().ListByChatIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        _memoryRepository.DidNotReceive().Add(Arg.Any<AskLucy.Domain.Memory.Memory>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
