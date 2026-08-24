using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Domain.Ai;
using FluentAssertions;
using FluentValidation;
using Hangfire;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/005-multi-provider-ai-engine T062 — the resolver is faked, proving the handler
/// resolves by the selected provider's key rather than depending on a single injected
/// <see cref="IAIProvider"/> (research.md Decision 3).
/// </summary>
public sealed class SendChatMessageCommandHandlerTests
{
    private readonly IAIProvider _resolvedProvider = Substitute.For<IAIProvider>();
    private readonly IAIProviderResolver _resolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IConversationKnowledgeBaseRepository _conversationKnowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IMemoryService _memoryService = Substitute.For<IMemoryService>();
    private readonly ILocationResolutionService _locationResolutionService = Substitute.For<ILocationResolutionService>();
    private readonly IViewerZoomDetector _viewerZoomDetector = Substitute.For<IViewerZoomDetector>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly SendChatMessageCommandHandler _handler;
    private readonly AIProvider _openAiProvider;
    private readonly AIModel _gpt41;
    private readonly Guid _chatId = Guid.NewGuid();

    public SendChatMessageCommandHandlerTests()
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

        // No knowledge bases attached by default — RAG retrieval never runs, matching every
        // existing test's expectation of unaugmented chat behavior (US1 AC2/AC3).
        _conversationKnowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AskLucy.Domain.Retrieval.ConversationKnowledgeBase>());

        // NSubstitute's default for an unconfigured `string?` property is "" (not null), so
        // ICurrentUserAccessor.UserId is never null here — the handler's `userId is not null`
        // memory-lookup gate always runs. Stub a benign NoneRelevant outcome so tests that
        // aren't exercising the Memory System don't NRE on an unconfigured Task<T> substitute.
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));

        // specs/037-location-query-resolution — default to NoIntent so pre-existing tests
        // that don't exercise location resolution continue to pass unchanged.
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<AskLucy.Domain.Chats.ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.NoIntent, null, null));

        _handler = new SendChatMessageCommandHandler(
            _resolver, _providers, _models, _conversationKnowledgeBases, _ragService, _memoryService,
            _locationResolutionService, _viewerZoomDetector, _userChatRepository, _currentUser, _backgroundJobClient,
            Microsoft.Extensions.Options.Options.Create(new LocationResolutionOptions()),
            new SendChatMessageCommandValidator(_providers, _models));
    }

    [Fact]
    public async Task Handle_ShouldResolveByTheSelectedProvidersKey_AndStreamItsChunks()
    {
        _resolvedProvider
            .StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable([new StreamChunk("Hello"), new StreamChunk(" world")]));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Hi")], _openAiProvider.Id, _gpt41.Id, null);

        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in _handler.Handle(command, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // A trailing metadata-only chunk (null ContentDelta) always follows now that the Memory
        // System's outcome rides the final chunk (see SendChatMessageCommandHandler) — filter to
        // the actual content deltas, which is what this test cares about.
        chunks.Where(c => c.ContentDelta is not null).Select(c => c.ContentDelta).Should().Equal("Hello", " world");
        _resolver.Received(1).Resolve("openai");
    }

    [Fact]
    public async Task Handle_ShouldThrowValidationException_WhenMessagesAreEmpty()
    {
        var command = new SendChatMessageCommand(_chatId, [], _openAiProvider.Id, _gpt41.Id, null);

        var act = async () =>
        {
            await foreach (var _ in _handler.Handle(command, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_ShouldThrowValidationException_WhenProviderIsNotEnabled()
    {
        var disabledProvider = AIProvider.Create("anthropic", "Anthropic", "test");
        _providers.GetByIdAsync(disabledProvider.Id, Arg.Any<CancellationToken>()).Returns(disabledProvider);

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Hi")], disabledProvider.Id, _gpt41.Id, null);

        var act = async () =>
        {
            await foreach (var _ in _handler.Handle(command, CancellationToken.None))
            {
            }
        };

        await act.Should().ThrowAsync<ValidationException>();
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
