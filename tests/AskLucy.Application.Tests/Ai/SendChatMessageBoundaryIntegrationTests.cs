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
    private readonly BoundaryScoringOptions _boundaryScoringOptions = new();
    private readonly ILogger<SendChatMessageCommandHandler> _logger = Substitute.For<ILogger<SendChatMessageCommandHandler>>();
    private readonly SendChatMessageCommandHandler _handler;
    private readonly AIProvider _openAiProvider;
    private readonly AIModel _gpt41;
    private readonly Guid _chatId = Guid.NewGuid();
    private IReadOnlyList<ChatMessage>? _capturedMessages;

    public SendChatMessageBoundaryIntegrationTests()
    {
        // [LoggerMessage]-generated code checks IsEnabled first and skips Log entirely when
        // false, which a bare substitute returns — without this, every logging assertion
        // below would silently observe nothing.
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

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
            Microsoft.Extensions.Options.Options.Create(_boundaryScoringOptions),
            _logger,
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

    /// <summary>
    /// specs/044-location-viewer-regression T005 (FR-001/FR-002) — the headline regression.
    /// Boundary resolution is an OPTIONAL enhancement sitting on the critical path of a MANDATORY
    /// outcome. When it throws, the user saw Lucy say "I've located Al Safa Park 2" while the
    /// viewer stayed empty, because the exception escaped the iterator before the chunk carrying
    /// ConfirmedLocation was ever yielded — taking __LOCATION__, assistant-message persistence
    /// and [DONE] with it.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldStillDeliverTheConfirmedLocation_WhenBoundaryResolutionThrows()
    {
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);

        var confirmedLocation = new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9);
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, confirmedLocation, "I've located Al Safa Park 2."));

        _boundaryResolutionService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<BoundaryResolutionOutcome>(_ => throw new HttpRequestException("vision/imagery call blew up"));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], _openAiProvider.Id, _gpt41.Id, null);

        var act = async () => await CollectAsync(command);
        await act.Should().NotThrowAsync("a failing optional boundary step must never terminate the chat turn");

        var chunks = await CollectAsync(command);
        chunks.Should().ContainSingle(c => c.ConfirmedLocation != null)
            .Subject.ConfirmedLocation!.LocationName.Should().Be("Al Safa Park 2");
        chunks.Should().NotContain(c => c.ConfirmedBoundary != null);
    }

    public static TheoryData<Exception> BoundaryFailures() =>
    [
        new HttpRequestException("network"),
        new System.Text.Json.JsonException("bad payload"),
        new InvalidOperationException("bad state"),
        new IndexOutOfRangeException("empty ranked list"),
    ];

    /// <summary>
    /// specs/044 T011 (FR-002/FR-005/FR-006) — FR-002 covers ANY exception type at any depth, not
    /// just network faults, and every one of them must still leave the user told *why* the outline
    /// is missing (never implying the location itself failed). T019 covers only the budget-expiry
    /// path; without this the throw path's user-facing outcome went unverified.
    /// </summary>
    [Theory]
    [MemberData(nameof(BoundaryFailures))]
    public async Task Handle_ShouldDeliverLocationAndExplainTheMissingOutline_ForAnyBoundaryFailure(Exception failure)
    {
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);

        var confirmedLocation = new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9);
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, confirmedLocation, "I've located Al Safa Park 2."));

        _boundaryResolutionService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<BoundaryResolutionOutcome>(_ => throw failure);

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], _openAiProvider.Id, _gpt41.Id, null);
        var chunks = await CollectAsync(command);

        chunks.Should().ContainSingle(c => c.ConfirmedLocation != null)
            .Subject.ConfirmedLocation!.LocationName.Should().Be("Al Safa Park 2");
        chunks.Should().NotContain(c => c.ConfirmedBoundary != null);

        // FR-006: the user is told the OUTLINE is unavailable, in wording that never suggests the
        // location lookup failed — the existing boundary template says "site boundary", not "that".
        chunks.Should().Contain(c => c.ContentDelta == BoundaryConfirmationTemplates.Unavailable);
        BoundaryConfirmationTemplates.Unavailable.Should().Contain("site boundary");
    }

    /// <summary>
    /// specs/044 T014 (FR-009a) — the stale-state repair, asserted at the handler seam: a failing
    /// boundary for a NEW site must not leave the previous site's boundary in play. The clear
    /// itself is atomic inside RecordActiveLocationCommandHandler; here we prove the handler still
    /// attempts resolution for the new site and emits no boundary for it.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldResolveForTheNewSiteAndEmitNoBoundary_WhenTheNewSitesBoundaryFails()
    {
        var chat = ChatWithActiveBoundary("Al Safa Park 2");
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns(chat);

        var confirmedLocation = new ConfirmedLocationData(25.24, 55.30, "Zabeel Park", 0.88);
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, confirmedLocation, "I've located Zabeel Park."));

        _boundaryResolutionService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<BoundaryResolutionOutcome>(_ => throw new HttpRequestException("overpass down"));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Zabeel Park")], _openAiProvider.Id, _gpt41.Id, null);
        var chunks = await CollectAsync(command);

        await _boundaryResolutionService.Received(1).ResolveAsync(confirmedLocation, _chatId, Arg.Any<CancellationToken>());
        chunks.Should().ContainSingle(c => c.ConfirmedLocation != null)
            .Subject.ConfirmedLocation!.LocationName.Should().Be("Zabeel Park");
        chunks.Should().NotContain(c => c.ConfirmedBoundary != null);
    }

    /// <summary>
    /// specs/044 T012-adjacent (FR-001a) — the location chunk must be yielded BEFORE the boundary
    /// step is entered. Asserted at the handler seam by recording the interleaving; the
    /// controller-level counterpart lives in AiControllerChatStreamTests.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldYieldTheLocationChunk_BeforeEnteringTheBoundaryStep()
    {
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);

        var confirmedLocation = new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9);
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, confirmedLocation, "I've located Al Safa Park 2."));

        var sequence = new List<string>();
        _boundaryResolutionService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sequence.Add("boundary-step-entered");
                return new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Unavailable, null, null);
            });

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], _openAiProvider.Id, _gpt41.Id, null);

        await foreach (var chunk in _handler.Handle(command, TestContext.Current.CancellationToken))
        {
            if (chunk.ConfirmedLocation is not null)
            {
                sequence.Add("location-chunk-yielded");
            }
        }

        sequence.Should().Equal("location-chunk-yielded", "boundary-step-entered");
    }

    private void ArrangeConfirmedLocation(out ConfirmedLocationData confirmedLocation)
    {
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        confirmedLocation = new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9);
        _locationResolutionService.ResolveAsync(Arg.Any<string?>(), _chatId, Arg.Any<string>(), Arg.Any<ActiveSiteLocation?>(), Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(LocationResolutionOutcomeType.Confirmed, confirmedLocation, "I've located Al Safa Park 2."));
    }

    /// <summary>
    /// specs/044 T015 (FR-003, SC-007) — the fail-slow half. Per-dependency timeouts sum to ~90s
    /// with nothing capping the total, so a hung boundary step used to hold the turn open far past
    /// any proxy idle timeout. The budget being configurable is what makes this testable in
    /// milliseconds rather than requiring a 45-second test.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldAbandonTheBoundaryAndCompleteTheTurn_WhenTheStepExceedsItsBudget()
    {
        _boundaryScoringOptions.BoundaryTimeoutSeconds = 1;
        ArrangeConfirmedLocation(out _);

        _boundaryResolutionService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => HangUntilCancelledAsync(call.ArgAt<CancellationToken>(2)));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], _openAiProvider.Id, _gpt41.Id, null);

        var started = DateTime.UtcNow;
        var chunks = await CollectAsync(command);
        var elapsed = DateTime.UtcNow - started;

        chunks.Should().ContainSingle(c => c.ConfirmedLocation != null);
        chunks.Should().NotContain(c => c.ConfirmedBoundary != null);
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15), "the turn must be bounded by the budget, not left hanging");

        // FR-006: the timeout is explained to the user, not silently swallowed.
        chunks.Should().Contain(c => c.ContentDelta == BoundaryConfirmationTemplates.Unavailable);
    }

    /// <summary>
    /// specs/044 T018 (FR-007, contract H-3) — the catch-ordering trap. Once a linked token is
    /// passed down, GeminiBoundaryVisionAnalyzer's identically-shaped guard sees it as "the caller
    /// cancelled" and rethrows on budget expiry; that is only correct because the handler
    /// re-adjudicates against the ORIGINAL request token. Reversing the two catches would report
    /// every user cancellation as a boundary timeout — a timeout-only test cannot catch that.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldPropagateCallerCancellation_RatherThanTreatingItAsABoundaryTimeout()
    {
        ArrangeConfirmedLocation(out _);

        using var callerCancellation = new CancellationTokenSource();
        _boundaryResolutionService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await callerCancellation.CancelAsync();
                await Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(2));
                return new BoundaryResolutionOutcome(BoundaryResolutionOutcomeType.Unavailable, null, null);
            });

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], _openAiProvider.Id, _gpt41.Id, null);

        var act = async () =>
        {
            await foreach (var _ in _handler.Handle(command, callerCancellation.Token))
            {
                // drain
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>(
            "caller cancellation is a user action, never a boundary failure to be swallowed");
    }

    /// <summary>
    /// Inspects received calls rather than using <c>Received().Log(...)</c>: [LoggerMessage]
    /// source-gen invokes <c>Log&lt;TState&gt;</c> with a generated struct as TState, so an
    /// argument-matcher overload infers <c>TState = object</c> and never matches the real call.
    /// (The same mismatch is why the unrelated McpObservabilityTests currently fail.)
    /// </summary>
    private List<(LogLevel Level, string Message, Exception? Exception)> LoggedWarnings() =>
        _logger.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(c => c.GetArguments())
            .Select(a => ((LogLevel)a[0]!, a[2]?.ToString() ?? string.Empty, a[3] as Exception))
            .Where(x => x.Item1 == LogLevel.Warning)
            .ToList();

    /// <summary>
    /// specs/044 T019 (FR-005, SC-006) — no silent failures. A boundary that was abandoned on
    /// budget must be diagnosable from the logs alone, and distinguishable from a provider failure:
    /// the two have different causes and different fixes.
    /// </summary>
    [Fact]
    public async Task Handle_ShouldLogTheTimeout_DistinguishablyFromAProviderFailure_WhenTheBudgetExpires()
    {
        _boundaryScoringOptions.BoundaryTimeoutSeconds = 1;
        ArrangeConfirmedLocation(out _);

        _boundaryResolutionService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => HangUntilCancelledAsync(call.ArgAt<CancellationToken>(2)));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], _openAiProvider.Id, _gpt41.Id, null);
        await CollectAsync(command);

        var warnings = LoggedWarnings();
        warnings.Should().ContainSingle(w => w.Message.Contains("budget", StringComparison.OrdinalIgnoreCase),
            "actual warnings were: {0}", string.Join(" | ", warnings.Select(w => $"{w.Message} [ex={w.Exception?.GetType().Name}]")));
        warnings.Should().NotContain(w => w.Exception != null,
            "a budget expiry is not a provider failure and must not be logged as one");
    }

    [Fact]
    public async Task Handle_ShouldLogTheCause_WhenTheBoundaryStepThrows()
    {
        ArrangeConfirmedLocation(out _);

        _boundaryResolutionService.ResolveAsync(Arg.Any<ConfirmedLocationData>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<BoundaryResolutionOutcome>(_ => throw new HttpRequestException("overpass down"));

        var command = new SendChatMessageCommand(_chatId, [new ChatMessageDto("user", "Show me Al Safa Park 2")], _openAiProvider.Id, _gpt41.Id, null);
        await CollectAsync(command);

        // The exception itself must reach the log — "failed" with no cause is not diagnosable.
        var warnings = LoggedWarnings();
        warnings.Should().ContainSingle(w => w.Exception is HttpRequestException);
        warnings.Should().NotContain(w => w.Message.Contains("budget", StringComparison.OrdinalIgnoreCase),
            "a provider failure is not a timeout and must not be logged as one");
    }

    private static async Task<BoundaryResolutionOutcome> HangUntilCancelledAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("unreachable");
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
