using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Memory;
using AskLucy.Application.Memory.Commands.ApproveMemory;
using AskLucy.Application.Memory.Commands.RejectMemory;
using AskLucy.Application.Tests.Ai;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Memory;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using MemoryEntity = AskLucy.Domain.Memory.Memory;

namespace AskLucy.Application.Tests.Memory;

/// <summary>
/// tasks.md T057 (US3 AC1–AC5) — exercises <see cref="MemoryExtractionJob"/> end to end against a
/// faked <see cref="IAIProvider"/> classification response, proving each configured
/// <see cref="MemoryApprovalMode"/> produces the correct outcome, plus the approve/reject command
/// handlers' own state transitions.
/// </summary>
public sealed class ApprovalWorkflowTests
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
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IMemoryVectorStore _vectorStore = Substitute.For<IMemoryVectorStore>();
    private readonly IAIProviderRepository _aiProviderRepository = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _aiModelRepository = Substitute.For<IAIModelRepository>();
    private readonly IAIProviderResolver _aiProviderResolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProvider _aiProvider = Substitute.For<IAIProvider>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly MemoryExtractionJob _job;
    private const string UserId = "user-1";

    public ApprovalWorkflowTests()
    {
        var provider = AIProvider.Create("openai", "OpenAI", "test");
        provider.SetCredential("ciphertext", "test");
        provider.Enable("test");
        var model = AIModel.Create(
            provider.Id, "gpt-4.1", "GPT-4.1", 128000, 16384,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");

        _aiProviderRepository.ListEnabledAsync(Arg.Any<CancellationToken>()).Returns(new List<AIProvider> { provider });
        _aiProviderRepository.GetByIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(provider);
        _aiModelRepository.GetByIdAsync(model.Id, Arg.Any<CancellationToken>()).Returns(model);
        _aiModelRepository.ListAvailableByProviderIdAsync(provider.Id, Arg.Any<CancellationToken>()).Returns(new List<AIModel> { model });
        _aiProviderResolver.Resolve("openai").Returns(_aiProvider);

        var embeddingProvider = EmbeddingProvider.Create("openai", "text-embedding-3-small", 1536, EmbeddingHostingType.Cloud, true, "test");
        _embeddingProviderRepository.GetDefaultAsync(EmbeddingHostingType.Cloud, Arg.Any<CancellationToken>()).Returns(embeddingProvider);
        _embeddingServiceResolver.Resolve("openai").Returns(_embeddingService);
        _embeddingService.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new EmbeddingResult(new float[1536], 1536));

        var defaultProviderResolver = CapabilityResolverTestFactory.Unassigned(_aiProviderRepository, _aiModelRepository);

        _job = new MemoryExtractionJob(
            _userChatRepository, _messageRepository, _memoryRepository, _preferenceRepository, _approvalRepository,
            _auditLogRepository, _memoryEmbeddingRepository, _conflictDetectionService, _notifier,
            _embeddingProviderRepository, _embeddingServiceResolver, _vectorStore,
            _aiProviderRepository, _aiModelRepository, _aiProviderResolver, defaultProviderResolver,
            _unitOfWork, Substitute.For<ILogger<MemoryExtractionJob>>());
    }

    private void SetUpChatWithOneCandidateMessage(out UserChat chat)
    {
        chat = UserChat.Create("Test chat", UserId, null, UserId);
        _userChatRepository.GetByIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(chat);

        var message = Message.Create(chat.Id, MessageRole.User, MessageKind.Text, "I use PostgreSQL for everything.", null, UserId);
        _messageRepository.ListByChatIdAsync(chat.Id, Arg.Any<CancellationToken>()).Returns(new List<Message> { message });

        _aiProvider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult(
                """[{"content":"Uses PostgreSQL for everything","category":"PersonalFact","isExplicit":true,"isSensitive":false,"confidence":0.9}]""",
                new ChatUsage(null, null, null, null, null)));
    }

    [Fact]
    public async Task RunAsync_ShouldHoldTheCandidateAsPendingApproval_WhenCategoryModeIsManual()
    {
        SetUpChatWithOneCandidateMessage(out var chat);
        var categoryPreference = MemoryCategoryPreference.CreateDefault(UserId, MemoryCategory.PersonalFact, "test");
        categoryPreference.Update(MemoryApprovalMode.Manual, null, "test");
        _preferenceRepository.GetCategoryPreferenceAsync(UserId, MemoryCategory.PersonalFact, Arg.Any<CancellationToken>()).Returns(categoryPreference);
        _memoryRepository.GetActiveByCategoryAsync(UserId, null, MemoryCategory.PersonalFact, Arg.Any<CancellationToken>()).Returns(new List<MemoryEntity>());

        await _job.RunAsync(chat.Id, CancellationToken.None);

        // A pending candidate is still embedded (harmless — SqlServerMemoryVectorStore's query
        // filters to State = 'Active', so it's simply not yet retrievable) so approval doesn't
        // need a separate re-embed step later; what matters here is the lifecycle state itself.
        _memoryRepository.Received(1).Add(Arg.Is<MemoryEntity>(m => m != null && m.State == MemoryLifecycleState.PendingApproval));
        await _notifier.DidNotReceive().NotifyAsync(
            Arg.Any<string>(), Arg.Any<Guid?>(), MemoryNotificationEventType.AutoApproved, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldActivateTheCandidateImmediately_WhenCategoryModeIsAutomatic()
    {
        SetUpChatWithOneCandidateMessage(out var chat);
        var categoryPreference = MemoryCategoryPreference.CreateDefault(UserId, MemoryCategory.PersonalFact, "test");
        _preferenceRepository.GetCategoryPreferenceAsync(UserId, MemoryCategory.PersonalFact, Arg.Any<CancellationToken>()).Returns(categoryPreference);
        _memoryRepository.GetActiveByCategoryAsync(UserId, null, MemoryCategory.PersonalFact, Arg.Any<CancellationToken>()).Returns(new List<MemoryEntity>());

        await _job.RunAsync(chat.Id, CancellationToken.None);

        _memoryRepository.Received(1).Add(Arg.Is<MemoryEntity>(m => m != null && m.State == MemoryLifecycleState.Active));
        _approvalRepository.Received(1).Add(Arg.Is<MemoryApproval>(a => a != null && a.Decision == MemoryApprovalDecision.Approved));
        await _notifier.Received(1).NotifyAsync(UserId, Arg.Any<Guid>(), MemoryNotificationEventType.AutoApproved, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldCreateNoCandidateAtAll_WhenCategoryModeIsDisabled()
    {
        SetUpChatWithOneCandidateMessage(out var chat);
        var categoryPreference = MemoryCategoryPreference.CreateDefault(UserId, MemoryCategory.PersonalFact, "test");
        categoryPreference.Update(MemoryApprovalMode.Disabled, null, "test");
        _preferenceRepository.GetCategoryPreferenceAsync(UserId, MemoryCategory.PersonalFact, Arg.Any<CancellationToken>()).Returns(categoryPreference);

        await _job.RunAsync(chat.Id, CancellationToken.None);

        _memoryRepository.DidNotReceive().Add(Arg.Any<MemoryEntity>());
    }

    [Fact]
    public async Task ApproveMemory_ShouldActivateAPendingCandidate()
    {
        var memory = MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.PersonalFact, "Candidate", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Manual, "test");
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(UserId);
        _memoryRepository.GetByIdAsync(memory.Id, Arg.Any<CancellationToken>()).Returns(memory);

        var handler = new ApproveMemoryCommandHandler(_memoryRepository, _approvalRepository, _auditLogRepository, _unitOfWork, currentUser);
        await handler.Handle(new ApproveMemoryCommand(memory.Id), CancellationToken.None);

        memory.State.Should().Be(MemoryLifecycleState.Active);
    }

    [Fact]
    public async Task RejectMemory_ShouldDiscardAPendingCandidate()
    {
        var memory = MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.PersonalFact, "Candidate", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Manual, "test");
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(UserId);
        _memoryRepository.GetByIdAsync(memory.Id, Arg.Any<CancellationToken>()).Returns(memory);

        var handler = new RejectMemoryCommandHandler(_memoryRepository, _approvalRepository, _auditLogRepository, _unitOfWork, currentUser);
        await handler.Handle(new RejectMemoryCommand(memory.Id), CancellationToken.None);

        memory.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveMemory_ShouldThrowConflict_WhenTheMemoryIsAlreadyActive()
    {
        var memory = MemoryEntity.CreateCandidate(
            UserId, null, MemoryCategory.PersonalFact, "Already active", MemorySourceType.PassiveConversationAnalysis,
            null, 0.5m, 0.5m, isSensitive: false, MemoryApprovalMode.Automatic, "test");
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(UserId);
        _memoryRepository.GetByIdAsync(memory.Id, Arg.Any<CancellationToken>()).Returns(memory);

        var handler = new ApproveMemoryCommandHandler(_memoryRepository, _approvalRepository, _auditLogRepository, _unitOfWork, currentUser);
        var act = () => handler.Handle(new ApproveMemoryCommand(memory.Id), CancellationToken.None);

        await act.Should().ThrowAsync<MemoryNotPendingApprovalException>();
    }
}
