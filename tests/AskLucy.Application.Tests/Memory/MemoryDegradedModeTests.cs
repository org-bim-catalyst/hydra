using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.Retrieval;
using FluentAssertions;
using Hangfire;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Memory;

/// <summary>tasks.md T039 (clarified 2026-08-09 Q1, FR-014a) — the memory subsystem being unavailable at response time degrades gracefully: the chat still generates in full, without memory context, and is never blocked or delayed by the failure.</summary>
public sealed class MemoryDegradedModeTests
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
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly SendChatMessageCommandHandler _handler;
    private readonly AIProvider _openAiProvider;
    private readonly AIModel _gpt41;
    private readonly Guid _chatId = Guid.NewGuid();

    public MemoryDegradedModeTests()
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
            .Returns(ToAsyncEnumerable([new StreamChunk("Here's an answer, no memory needed.")]));

        _conversationKnowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<ConversationKnowledgeBase>());
        _currentUser.UserId.Returns("user-1");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>())
            .Returns(UserChat.Create("Test chat", "user-1", null, "user-1"));

        // IMemoryService itself never throws (its own contract) — it returns Unavailable, which
        // is exactly what the handler must tolerate without erroring or delaying the response.
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), _chatId, Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.Unavailable, null, [], "The memory service is temporarily unavailable."));

        _handler = new SendChatMessageCommandHandler(
            _resolver, _providers, _models, _conversationKnowledgeBases, _ragService, _memoryService, _userChatRepository,
            _currentUser, _backgroundJobClient, new SendChatMessageCommandValidator(_providers, _models));
    }

    [Fact]
    public async Task Handle_ShouldStillReturnAFullResponse_WithNoMemoryContext_WhenMemoryIsUnavailable()
    {
        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "What do you remember about me?")], _openAiProvider.Id, _gpt41.Id, null);

        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in _handler.Handle(command, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Never blocked — content still streams in full, unaugmented (no memory system message).
        chunks.Select(c => c.ContentDelta).Should().Contain("Here's an answer, no memory needed.");
        _resolvedProvider.Received(1).StreamChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(m => m.Count == 1 && m[0].Role == ChatRole.User),
            "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());

        // The failure rides the final chunk (visible, non-silent) rather than being swallowed.
        var finalChunk = chunks.Should().ContainSingle(c => c.MemoryOutcome != null).Subject;
        finalChunk.MemoryOutcome!.Type.Should().Be(MemoryRetrievalOutcomeType.Unavailable);
        finalChunk.MemoryOutcome.UnavailableReason.Should().NotBeNullOrEmpty();
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
