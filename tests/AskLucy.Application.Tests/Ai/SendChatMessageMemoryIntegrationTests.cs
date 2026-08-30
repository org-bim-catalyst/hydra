using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>tasks.md T037 (US1 AC1, AC3) — <see cref="SendChatMessageCommandHandler"/> inserts the memory context message ahead of RAG's own (research.md Decision 2) when both apply, and omits it entirely when memory found nothing relevant.</summary>
public sealed class SendChatMessageMemoryIntegrationTests
{
    private readonly IAIProvider _resolvedProvider = Substitute.For<IAIProvider>();
    private readonly IAIProviderResolver _resolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IConversationKnowledgeBaseRepository _conversationKnowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IMemoryService _memoryService = Substitute.For<IMemoryService>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly ILocationResolutionService _locationResolutionService = Substitute.For<ILocationResolutionService>();
    private readonly IBoundaryResolutionService _boundaryResolutionService = Substitute.For<IBoundaryResolutionService>();
    private readonly IViewerZoomDetector _viewerZoomDetector = Substitute.For<IViewerZoomDetector>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly BoundaryScoringOptions _boundaryScoringOptions = new();
    private readonly SendChatMessageCommandHandler _handler;
    private readonly AIProvider _openAiProvider;
    private readonly AIModel _gpt41;
    private readonly Guid _chatId = Guid.NewGuid();

    public SendChatMessageMemoryIntegrationTests()
    {
        _openAiProvider = AIProvider.Create("openai", "OpenAI", "test");
        _openAiProvider.SetCredential("ciphertext", "test");
        _openAiProvider.Enable("test");

        _gpt41 = AIModel.Create(
            _openAiProvider.Id, "gpt-4.1", "GPT-4.1", 128000, 16384,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");

        _providers.GetByIdAsync(_openAiProvider.Id, Arg.Any<CancellationToken>()).Returns(_openAiProvider);
        _models.GetByIdAsync(_gpt41.Id, Arg.Any<CancellationToken>()).Returns(_gpt41);
        _resolver.Resolve("openai").Returns(_resolvedProvider);
        _resolvedProvider
            .StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable([new StreamChunk("Answer.")]));

        _conversationKnowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<ConversationKnowledgeBase>());
        _currentUser.UserId.Returns("user-1");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>())
            .Returns(UserChat.Create("Test chat", "user-1", null, "user-1"));

        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<AskLucy.Domain.Chats.ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.NoIntent, null, null));

        _handler = new SendChatMessageCommandHandler(
            _resolver, _providers, _models, _conversationKnowledgeBases, _ragService, _memoryService,
            _locationResolutionService, _boundaryResolutionService, _viewerZoomDetector, _userChatRepository, _currentUser, _backgroundJobClient,
            Microsoft.Extensions.Options.Options.Create(new LocationResolutionOptions()),
            Microsoft.Extensions.Options.Options.Create(_boundaryScoringOptions),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SendChatMessageCommandHandler>.Instance,
            new SendChatMessageCommandValidator(_providers, _models));
    }

    [Fact]
    public async Task Handle_ShouldInsertMemoryContextBeforeRagContext_WhenBothApply()
    {
        var knowledgeBaseId = Guid.NewGuid();
        _conversationKnowledgeBases.GetByConversationAsync(_chatId, Arg.Any<CancellationToken>())
            .Returns(new List<ConversationKnowledgeBase> { ConversationKnowledgeBase.Create(_chatId, knowledgeBaseId, "test") });

        var citation = new RagCitationContext(Guid.NewGuid(), knowledgeBaseId, Guid.NewGuid(), Guid.NewGuid(), "Doc.pdf", "KB", null, null, "RAG excerpt.");
        _ragService.RetrieveContextAsync(_chatId, Arg.Any<string>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new RagRetrievalOutcome(RagRetrievalOutcomeType.Grounded, "RAG excerpt.", [citation], null));

        var memoryId = Guid.NewGuid();
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), _chatId, Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(
                MemoryRetrievalOutcomeType.Found, "The user prefers React.",
                [new MemoryReferenceContext(memoryId, "The user prefers React.", 0.9m)], null));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "What's in my docs?")], _openAiProvider.Id, _gpt41.Id, null);

        await foreach (var _ in _handler.Handle(command, CancellationToken.None))
        {
        }

        _resolvedProvider.Received(1).StreamChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(m =>
                m != null
                && m.Count == 3
                && m[0].Role == ChatRole.System && m[0].Content.Contains("The user prefers React.")
                && m[1].Role == ChatRole.System && m[1].Content.Contains("RAG excerpt.")),
            "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldOmitMemoryContext_WhenNoneRelevant()
    {
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), _chatId, Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Hi")], _openAiProvider.Id, _gpt41.Id, null);

        await foreach (var _ in _handler.Handle(command, CancellationToken.None))
        {
        }

        _resolvedProvider.Received(1).StreamChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(m => m != null && m.Count == 1 && m[0].Role == ChatRole.User),
            "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldEnqueueMemoryExtraction_AfterStreamingCompletes()
    {
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), _chatId, Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Hi")], _openAiProvider.Id, _gpt41.Id, null);

        await foreach (var _ in _handler.Handle(command, CancellationToken.None))
        {
        }

        _backgroundJobClient.Received(1).Create(
            Arg.Is<Job>(j => j != null && j.Type == typeof(IMemoryExtractionJob)), Arg.Any<IState>());
    }

    private static async IAsyncEnumerable<StreamChunk> ToAsyncEnumerable(IEnumerable<StreamChunk> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }
}
