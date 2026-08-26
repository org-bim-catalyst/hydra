using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;
using FluentAssertions;
using Hangfire;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/042-site-boundary-resolution T032 — <see cref="SendChatMessageCommandHandler"/>'s
/// boundary-resolution trigger logic (research.md #11, contracts/chat-pipeline-integration.md):
/// resolves only when the confirmed site differs from <see cref="UserChat.ActiveBoundary"/>,
/// injects a context system message (with FR-010 correction guidance) whenever a boundary is
/// already active, and never re-resolves for a repeated reference to the same site (FR-009).
/// </summary>
public sealed class SendChatMessageBoundaryIntegrationTests
{
    private readonly IAIProvider _resolvedProvider = Substitute.For<IAIProvider>();
    private readonly IAIProviderResolver _resolver = Substitute.For<IAIProviderResolver>();
    private readonly IAIProviderRepository _providers = Substitute.For<IAIProviderRepository>();
    private readonly IAIModelRepository _models = Substitute.For<IAIModelRepository>();
    private readonly IConversationKnowledgeBaseRepository _conversationKnowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IMemoryService _memoryService = Substitute.For<IMemoryService>();
    private readonly ILocationResolutionService _locationResolutionService = Substitute.For<ILocationResolutionService>();
    private readonly IBoundaryResolutionService _boundaryResolutionService = Substitute.For<IBoundaryResolutionService>();
    private readonly IViewerZoomDetector _viewerZoomDetector = Substitute.For<IViewerZoomDetector>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly SendChatMessageCommandHandler _handler;
    private readonly AIProvider _openAiProvider;
    private readonly AIModel _gpt41;
    private readonly Guid _chatId = Guid.NewGuid();
    private IReadOnlyList<ChatMessage>? _capturedMessages;

    public SendChatMessageBoundaryIntegrationTests()
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
            .Returns(call =>
            {
                _capturedMessages = call.ArgAt<IReadOnlyList<ChatMessage>>(0);
                return ToAsyncEnumerable([new StreamChunk("Here you go.")]);
            });

        _conversationKnowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AskLucy.Domain.Retrieval.ConversationKnowledgeBase>());

        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));

        _handler = new SendChatMessageCommandHandler(
            _resolver, _providers, _models, _conversationKnowledgeBases, _ragService, _memoryService,
            _locationResolutionService, _boundaryResolutionService, _viewerZoomDetector, _userChatRepository, _currentUser, _backgroundJobClient,
            Microsoft.Extensions.Options.Options.Create(new LocationResolutionOptions()),
            new SendChatMessageCommandValidator(_providers, _models));
    }

    private static UserChat ChatWithActiveBoundary(string siteName)
    {
        var chat = UserChat.Create("Boundary chat", "user-1", null, "user-1");
        chat.SetActiveBoundary(
            siteName, 25.156, 55.2218,
            [new GeoPoint(25.156, 55.221), new GeoPoint(25.156, 55.222), new GeoPoint(25.155, 55.222)],
            15_000, 0.9, BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary, "OpenStreetMap (leisure=park)", "user-1");
        return chat;
    }

    [Fact]
    public async Task Handle_ShouldNotCallBoundaryResolution_WhenTheSameSiteIsReferencedAgain()
    {
        var chat = ChatWithActiveBoundary("Al Safa Park 2");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);

        var confirmedLocation = new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9);
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, confirmedLocation, "I've located Al Safa Park 2."));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Tell me about Al Safa Park 2 again")], _openAiProvider.Id, _gpt41.Id, null);
        await CollectAsync(command);

        await _boundaryResolutionService.DidNotReceive().ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldInjectContextSystemMessage_WhenABoundaryIsAlreadyActive()
    {
        var chat = ChatWithActiveBoundary("Al Safa Park 2");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.NoIntent, null, null));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "How sure are you about that boundary?")], _openAiProvider.Id, _gpt41.Id, null);
        await CollectAsync(command);

        _capturedMessages.Should().NotBeNull();
        _capturedMessages!.Should().Contain(m =>
            m.Role == ChatRole.System &&
            m.Content.Contains("Al Safa Park 2") &&
            m.Content.Contains("High") &&
            m.Content.Contains("do not claim you cannot access it"));
    }

    [Fact]
    public async Task Handle_ShouldNotInjectContextSystemMessage_WhenNoBoundaryIsActive()
    {
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.NoIntent, null, null));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "What is the weather like?")], _openAiProvider.Id, _gpt41.Id, null);
        await CollectAsync(command);

        _capturedMessages.Should().NotBeNull();
        _capturedMessages!.Should().NotContain(m => m.Content.Contains("active site boundary"));
    }

    [Fact]
    public async Task Handle_ShouldResolveANewBoundary_WhenADifferentSiteIsConfirmed()
    {
        var chat = ChatWithActiveBoundary("Al Safa Park 2");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);

        var confirmedLocation = new ConfirmedLocationData(25.24, 55.30, "Zabeel Park", 0.88);
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, confirmedLocation, "I've located Zabeel Park."));

        var newBoundary = new ConfirmedSiteBoundaryData(
            "Zabeel Park", 25.24, 55.30,
            [new GeoPoint(25.24, 55.30), new GeoPoint(25.241, 55.301), new GeoPoint(25.239, 55.301)],
            20_000, 0.9, BoundaryConfidenceLevel.High, SiteBoundarySource.OsmBoundary, "OpenStreetMap (leisure=park)", []);
        _boundaryResolutionService.ResolveAsync(confirmedLocation, _chatId, Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, newBoundary, "I've outlined Zabeel Park's boundary."));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Zabeel Park")], _openAiProvider.Id, _gpt41.Id, null);
        var chunks = await CollectAsync(command);

        await _boundaryResolutionService.Received(1).ResolveAsync(confirmedLocation, _chatId, Arg.Any<CancellationToken>());
        chunks.Should().Contain(c => c.ContentDelta == "I've outlined Zabeel Park's boundary.");
        chunks.Should().ContainSingle(c => c.ConfirmedBoundary != null).Subject.ConfirmedBoundary!.SiteName.Should().Be("Zabeel Park");
    }

    private async Task<List<ChatStreamChunk>> CollectAsync(SendChatMessageCommand command)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in _handler.Handle(command, CancellationToken.None))
            chunks.Add(chunk);
        return chunks;
    }

    private static async IAsyncEnumerable<StreamChunk> ToAsyncEnumerable(IEnumerable<StreamChunk> items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }
}
