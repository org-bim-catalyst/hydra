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
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// specs/045-conversational-agent-runtime T006 — a characterisation harness for the extraction
/// in Phase 2a.
///
/// <para>
/// <b>Why this exists when six SendChatMessage* test files already do.</b> Those pin individual
/// behaviours — that a boundary failure still delivers the location, that caller cancellation is
/// not recorded as a timeout, that memory context precedes RAG context. Each asserts on a chunk
/// it selects out of the stream. None asserts on the <i>shape of the whole emitted sequence</i>,
/// which is precisely what a refactor is most likely to disturb and least likely to be caught
/// disturbing: reorder two yields and every existing test still passes, because each one goes
/// looking for its own chunk rather than reading the transcript.
/// </para>
///
/// <para>
/// specs/044 was the regression that established this ordering, at real cost — a failing boundary
/// step took <c>__LOCATION__</c>, assistant-message persistence and <c>[DONE]</c> down with it,
/// and a slow one held the viewer for up to ~90 s. The fix was to guarantee that no network call
/// sits between resolving a location and delivering it. That guarantee is an ordering property,
/// so it needs an ordering test, and it must survive
/// <c>ConversationTurnOrchestrator</c> taking over the body.
/// </para>
///
/// <para>
/// These tests are <b>behaviour-neutral by construction</b>: they were written against the
/// pre-extraction handler and must pass unchanged afterwards (T010). If one fails during the
/// extraction, the extraction changed behaviour — that is the signal, not a reason to edit the
/// test.
/// </para>
/// </summary>
public sealed class SendChatMessageCommandHandlerCharacterisationTests
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
    private readonly ILogger<SendChatMessageCommandHandler> _logger = Substitute.For<ILogger<SendChatMessageCommandHandler>>();
    private readonly SendChatMessageCommandHandler _handler;
    private readonly AIProvider _openAiProvider;
    private readonly AIModel _model;
    private readonly Guid _chatId = Guid.NewGuid();

    public SendChatMessageCommandHandlerCharacterisationTests()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        _openAiProvider = AIProvider.Create("openai", "OpenAI", "test");
        _openAiProvider.SetCredential("ciphertext", "test");
        _openAiProvider.Enable("test");

        _model = AIModel.Create(
            _openAiProvider.Id, "gpt-4.1", "GPT-4.1", 128000, 16384,
            new AIModelCapabilities(true, true, true, true, false, false, true, false, false), null, null, "test");

        _providers.GetByIdAsync(_openAiProvider.Id, Arg.Any<CancellationToken>()).Returns(_openAiProvider);
        _models.GetByIdAsync(_model.Id, Arg.Any<CancellationToken>()).Returns(_model);
        _resolver.Resolve("openai").Returns(_resolvedProvider);
        _resolvedProvider
            .StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsyncEnumerable([new StreamChunk("Reply text.")]));

        _conversationKnowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AskLucy.Domain.Retrieval.ConversationKnowledgeBase>());
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        _currentUser.UserId.Returns("user-1");

        _handler = SendChatMessageHandlerFactory.Create(
            _resolver, _providers, _models, _conversationKnowledgeBases, _ragService, _memoryService,
            _locationResolutionService, _boundaryResolutionService, _viewerZoomDetector, _userChatRepository,
            _currentUser, _backgroundJobClient,
            Microsoft.Extensions.Options.Options.Create(new LocationResolutionOptions()),
            Microsoft.Extensions.Options.Options.Create(new BoundaryScoringOptions()),
            _logger,
            new SendChatMessageCommandValidator(_providers, _models));
    }

    /// <summary>
    /// The full transcript for the platform's motivating turn. Pins the specs/044 ordering as a
    /// sequence rather than as isolated assertions: reply text, then the confirmation sentence,
    /// then the location chunk, then the message break announcing the boundary work, then the
    /// boundary confirmation, then the boundary payload. Any reordering fails here.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldEmitTheFullChunkSequenceInOrder_ForAConfirmedLocationAndNewBoundary()
    {
        var location = new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9);
        _locationResolutionService
            .ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, location, "I've located Al Safa Park 2."));

        var boundary = new ConfirmedSiteBoundaryData(
            "Al Safa Park 2", 25.156, 55.2218,
            [new GeoPoint(25.156, 55.221), new GeoPoint(25.156, 55.222), new GeoPoint(25.155, 55.222)],
            42_000, 0.85, BoundaryConfidenceLevel.Medium, SiteBoundarySource.OsmBoundary, "OpenStreetMap (leisure=park)", []);
        _boundaryResolutionService.ResolveAsync(location, _chatId, Arg.Any<CancellationToken>())
            .Returns(new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Confirmed, boundary, "I've outlined the site."));

        var chunks = await CollectAsync(new SendChatMessageCommand(
            _chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], _openAiProvider.Id, _model.Id, null));

        var shape = chunks.Select(Describe).ToList();

        shape.Should().ContainInOrder(
            "text:Reply text.",
            "text:I've located Al Safa Park 2.",
            "location:Al Safa Park 2",
            "break:Finding the site boundary",
            "text:I've outlined the site.",
            "boundary:Al Safa Park 2");

        // The load-bearing guarantee (specs/044 FR-001a): nothing that can fail or block sits
        // between resolving the location and delivering it.
        shape.IndexOf("location:Al Safa Park 2")
            .Should().BeLessThan(shape.IndexOf("break:Finding the site boundary"));
    }

    /// <summary>
    /// The break is announced BEFORE the boundary work, carrying a label — specs/044's fix for a
    /// reply that looked finished while up to 45 s of silent work continued behind it.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldAnnounceThePendingLabel_BeforeTheBoundaryStepRuns()
    {
        var location = new ConfirmedLocationData(25.24, 55.30, "Zabeel Park", 0.88);
        _locationResolutionService
            .ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, location, "I've located Zabeel Park."));

        var announcedBeforeResolution = false;
        _boundaryResolutionService.ResolveAsync(location, _chatId, Arg.Any<CancellationToken>())
            .Returns(_ => new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Unavailable, null, "Couldn't outline it."));

        await foreach (var chunk in _handler.Handle(
            new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Zabeel Park")], _openAiProvider.Id, _model.Id, null),
            CancellationToken.None))
        {
            if (chunk.StartsNewMessage && chunk.PendingLabel is not null)
            {
                announcedBeforeResolution =
                    _boundaryResolutionService.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IBoundaryResolutionService.ResolveAsync)) is false;
            }
        }

        announcedBeforeResolution.Should().BeTrue(
            "the pending label must reach the user before the boundary lookup starts, not after it finishes");
    }

    /// <summary>
    /// A turn with no location intent emits exactly the model's own text — no confirmation
    /// sentence, no location chunk, no break, no boundary call. The fast, ordinary case.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldEmitOnlyReplyText_WhenTheTurnHasNoLocationIntent()
    {
        _locationResolutionService
            .ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.NoIntent, null, null));

        var chunks = await CollectAsync(new SendChatMessageCommand(
            _chatId, [new ChatMessageDto("user", "What is a setback?")], _openAiProvider.Id, _model.Id, null));

        // The trailing outcome chunk is part of the real sequence: the handler always reports the
        // memory-retrieval outcome even when nothing was relevant, because the client needs it to
        // decide whether to render a memory trace. Pinned here rather than filtered out — a
        // characterisation test records what the code does, including the parts a refactor might
        // quietly drop.
        chunks.Select(Describe).Should().Equal("text:Reply text.", "outcome:memory=NoneRelevant");
        await _boundaryResolutionService.DidNotReceive()
            .ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Location resolution is launched before the model's stream is consumed, so geocoding
    /// overlaps generation rather than delaying the first token (specs/037 FR-008). The
    /// extraction must not serialise these.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldStartLocationResolution_BeforeDrainingTheModelStream()
    {
        var resolutionStarted = false;
        _locationResolutionService
            .ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                resolutionStarted = true;
                return Task.FromResult(new LocationResolutionOutcome(LocationResolutionOutcomeType.NoIntent, null, null));
            });

        _resolvedProvider
            .StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), "gpt-4.1", Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                resolutionStarted.Should().BeTrue("resolution must already be in flight when the model stream is consumed");
                return ToAsyncEnumerable([new StreamChunk("Reply text.")]);
            });

        await CollectAsync(new SendChatMessageCommand(
            _chatId, [new ChatMessageDto("user", "Hello")], _openAiProvider.Id, _model.Id, null));

        resolutionStarted.Should().BeTrue();
    }

    /// <summary>Memory extraction is enqueued exactly once, after everything else — never per chunk.</summary>
    [Fact]
    public async Task Handle_ShouldEnqueueMemoryExtractionExactlyOnce_AtTheEndOfTheTurn()
    {
        _locationResolutionService
            .ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.NoIntent, null, null));

        await CollectAsync(new SendChatMessageCommand(
            _chatId, [new ChatMessageDto("user", "Hello")], _openAiProvider.Id, _model.Id, null));

        _backgroundJobClient.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create))
            .Should().Be(1);
    }

    /// <summary>
    /// A stable, comparable description of one chunk — the vocabulary the sequence assertions are
    /// written in. Keeping it in one place means the tests read as a transcript rather than as a
    /// pile of null checks.
    /// </summary>
    private static string Describe(ChatStreamChunk chunk) =>
        chunk switch
        {
            { ConfirmedBoundary: not null } c => $"boundary:{c.ConfirmedBoundary.SiteName}",
            { ConfirmedLocation: not null } c => $"location:{c.ConfirmedLocation.LocationName}",
            { StartsNewMessage: true } c => $"break:{c.PendingLabel}",
            { ContentDelta: not null and not "" } c => $"text:{c.ContentDelta}",
            { ViewerZoom: not null } c => $"zoom:{c.ViewerZoom.Direction}",
            { MemoryOutcome: not null } c => $"outcome:memory={c.MemoryOutcome.Type}",
            { RetrievalOutcome: not null } c => $"outcome:retrieval={c.RetrievalOutcome.Type}",
            _ => "other",
        };

    private async Task<List<ChatStreamChunk>> CollectAsync(SendChatMessageCommand command)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in _handler.Handle(command, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static async IAsyncEnumerable<StreamChunk> ToAsyncEnumerable(IEnumerable<StreamChunk> chunks)
    {
        foreach (var chunk in chunks)
        {
            yield return chunk;
        }

        await Task.CompletedTask;
    }
}
