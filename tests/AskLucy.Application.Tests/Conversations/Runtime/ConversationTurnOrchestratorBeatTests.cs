using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Locations;
using AskLucy.Application.Options;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Chats;
using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AskLucy.Application.Tests.Conversations.Runtime;

/// <summary>
/// specs/045 US1 (T052) — the beats a turn actually produces, exercised against the real
/// <see cref="ConversationTurnOrchestrator"/> with <see cref="ITurnDecider"/> substituted at the
/// seam.
///
/// <para>
/// The decide step's own correctness (parsing, grounding, degradation) is
/// <see cref="TurnDecisionParserTests"/>'s job. These tests take a decision as given and ask what
/// the orchestrator does with it — which is the actual, user-visible behaviour this feature
/// exists to change: an acknowledgement before the work, a result written from the real outcome,
/// and the fast path costing nothing extra when there is nothing to do.
/// </para>
/// </summary>
public sealed class ConversationTurnOrchestratorBeatTests
{
    private readonly IConversationKnowledgeBaseRepository _knowledgeBases = Substitute.For<IConversationKnowledgeBaseRepository>();
    private readonly IRagService _ragService = Substitute.For<IRagService>();
    private readonly IMemoryService _memoryService = Substitute.For<IMemoryService>();
    private readonly IUserChatRepository _userChatRepository = Substitute.For<IUserChatRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IBackgroundJobClient _backgroundJobClient = Substitute.For<IBackgroundJobClient>();
    private readonly ITurnDecider _decider = Substitute.For<ITurnDecider>();
    private readonly ISuggestedActionOfferGenerator _offerGenerator = Substitute.For<ISuggestedActionOfferGenerator>();
    private readonly IAIProvider _provider = Substitute.For<IAIProvider>();
    private readonly Guid _chatId = Guid.NewGuid();

    public ConversationTurnOrchestratorBeatTests()
    {
        _knowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AskLucy.Domain.Retrieval.ConversationKnowledgeBase>());
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        _currentUser.UserId.Returns("user-1");

        _provider.StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable([new StreamChunk("plain reply")]));
    }

    private ConversationTurnOrchestrator BuildOrchestrator(IConversationCapability? capability = null)
    {
        var toolCatalog = new AgentToolCatalog(
            capability is null ? [] : [capability], new EmptyMcpToolRegistry());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions());
        var indexRetriever = new CapabilityIndexRetriever(
            Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance);
        var capabilityCatalog = new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions);

        var capabilityExecutor = new CapabilityExecutor(
            new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()),
            new PermissiveSchemaValidator(),
            runtimeOptions,
            NullLogger<CapabilityExecutor>.Instance);

        var flowCatalog = new ConversationFlowCatalog([]);
        var flowRunner = new FlowRunner(capabilityCatalog, capabilityExecutor, runtimeOptions, NullLogger<FlowRunner>.Instance);

        return new ConversationTurnOrchestrator(
            _knowledgeBases, _ragService, _memoryService, _userChatRepository, _currentUser,
            _backgroundJobClient, capabilityCatalog, flowCatalog, _decider, capabilityExecutor, flowRunner, _offerGenerator,
            NullLogger<ConversationTurnOrchestrator>.Instance);
    }

    private ConversationTurnRequest Request(string message) =>
        new(_chatId, [new ChatMessageDto("user", message)], _provider, "test-model", GenerationParameters: null);

    [Fact]
    public async Task RunAsync_ShouldEmitTheAcknowledgement_BeforeTheCapabilityRuns()
    {
        var capability = new StubCapability();
        var executionOrder = new List<string>();
        capability.OnExecute = () => executionOrder.Add("executed");

        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Act, [new TurnSlice("stub", "{}", "Doing the thing", null)]));
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                executionOrder.Add("narrated");
                return new ChatCompletionResult("It worked.", new ChatUsage(null, null, null, null, null));
            });

        var chunks = await CollectAsync(BuildOrchestrator(capability), Request("do the thing"));

        // FR-003: the acknowledgement is the FIRST thing yielded, before any capability work.
        chunks[0].StartsNewMessage.Should().BeTrue();
        chunks[1].ContentDelta.Should().Be(capability.AcknowledgementTemplate);

        executionOrder.Should().Equal("executed", "narrated");
    }

    [Fact]
    public async Task RunAsync_ShouldWriteTheResult_FromTheRealOutcome_NotACannedTemplate()
    {
        var capability = new StubCapability { SucceedWith = """{"status":"done"}""" };
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Act, [new TurnSlice("stub", "{}", null, null)]));
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("Real narration mentioning the outcome.", new ChatUsage(null, null, null, null, null)));

        var chunks = await CollectAsync(BuildOrchestrator(capability), Request("do the thing"));

        chunks.Select(c => c.ContentDelta).Should().Contain("Real narration mentioning the outcome.");

        // The narration call was given the capability's actual JSON result, not a placeholder —
        // this is what "written from the real outcome" (FR-007) means operationally.
        await _provider.Received(1).ChatAsync(
            Arg.Is<IReadOnlyList<ChatMessage>>(m => m != null && m.Any(x => x.Content.Contains("done"))),
            Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldNeverNarrateSuccess_ForACapabilityThatFailed()
    {
        var capability = new StubCapability { FailWith = "the lookup timed out" };
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Act, [new TurnSlice("stub", "{}", null, null)]));

        string? narrationPromptSeen = null;
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                narrationPromptSeen = call.ArgAt<IReadOnlyList<ChatMessage>>(0)[0].Content;
                return new ChatCompletionResult("I couldn't do that — the lookup timed out.", new ChatUsage(null, null, null, null, null));
            });

        var chunks = await CollectAsync(BuildOrchestrator(capability), Request("do the thing"));

        // The narration prompt itself must be told the outcome did not succeed — this is the
        // mechanism that makes "never narrate success for a failure" possible at all.
        narrationPromptSeen.Should().Contain("did not succeed");
        chunks.Select(c => c.ContentDelta).Should().Contain(c => c != null && c.Contains("couldn't"));
    }

    [Fact]
    public async Task RunAsync_ShouldFallBackToTemplateWording_WhenTheNarrationCallItselfFails()
    {
        // FR-008: the fallback exists for when narration cannot run at all — never as the normal
        // path, but the user must still read SOMETHING true about what happened.
        var capability = new StubCapability { FailWith = "not found" };
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Act, [new TurnSlice("stub", "{}", null, null)]));
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns<ChatCompletionResult>(_ => throw new InvalidOperationException("provider down"));

        var chunks = await CollectAsync(BuildOrchestrator(capability), Request("do the thing"));

        chunks.Select(c => c.ContentDelta).Should().Contain(c => c != null && c.Contains("not found"));
    }

    [Fact]
    public async Task RunAsync_ShouldEmitNoBeats_OnTheFastPath()
    {
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(TurnDecision.AnswerOnly);

        var chunks = await CollectAsync(BuildOrchestrator(), Request("what is a setback?"));

        // No acknowledgement beat, no message break — an ordinary reply, exactly as before this
        // feature (FR-006).
        chunks.Should().NotContain(c => c.StartsNewMessage);
        chunks.Select(c => c.ContentDelta).Should().Contain("plain reply");
        await _provider.DidNotReceive().ChatAsync(
            Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ShouldTakeTheFastPathMechanics_WhenIntentIsSuggest()
    {
        // research.md D18 — TurnIntent.Suggest always takes the words-only mechanics (no beats,
        // no acknowledgement); this fixture registers no capability at all, so the offer step
        // that follows (US2) finds nothing offerable and is suppressed too — the plain-reply shape
        // asserted here is permanent, not an interim narrowing.
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Suggest, []));

        var chunks = await CollectAsync(BuildOrchestrator(), Request("do you know that place?"));

        chunks.Should().NotContain(c => c.StartsNewMessage);
        chunks.Select(c => c.ContentDelta).Should().Contain("plain reply");
    }

    [Fact]
    public async Task RunAsync_ShouldEnqueueMemoryExtraction_RegardlessOfWhichPathRan()
    {
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(TurnDecision.AnswerOnly);

        await CollectAsync(BuildOrchestrator(), Request("hello"));

        _backgroundJobClient.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create))
            .Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_ShouldEmitConfirmedLocation_WhenTheCapabilitysOwnKeyMatches()
    {
        // The adapter between capability JSON and the frozen viewer payload (FR-048) — proven
        // against the actual capability key, not a stand-in.
        var locationService = Substitute.For<ILocationResolutionService>();
        var realCapability = new ResolveLocationCapability(locationService);
        locationService.ResolveQueryAsync(_chatId, "Al Safa Park 2", Arg.Any<CancellationToken>())
            .Returns(new LocationResolutionOutcome(
                LocationResolutionOutcomeType.Confirmed,
                new ConfirmedLocationData(25.156, 55.2218, "Al Safa Park 2", 0.9),
                "confirmed"));

        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Act,
                [new TurnSlice(ResolveLocationCapability.CapabilityKey, """{"query":"Al Safa Park 2"}""", null, null)]));
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("Found it.", new ChatUsage(null, null, null, null, null)));

        var chunks = await CollectAsync(BuildOrchestrator(realCapability), Request("show me Al Safa Park 2"));

        chunks.Should().ContainSingle(c => c.ConfirmedLocation != null)
            .Which.ConfirmedLocation!.LocationName.Should().Be("Al Safa Park 2");
    }

    private static async Task<List<ChatStreamChunk>> CollectAsync(ConversationTurnOrchestrator orchestrator, ConversationTurnRequest request)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in orchestrator.RunAsync(request, CancellationToken.None))
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

    private sealed class EmptyMcpToolRegistry : IMcpToolRegistry
    {
        public IReadOnlyCollection<IAgentTool> ActiveTools => [];

        public Task InvalidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Accepts everything — these tests are about orchestrator beats, not grounding, which CapabilityContractTests/CapabilityExecutorTests already cover.</summary>
    private sealed class PermissiveSchemaValidator : IJsonSchemaValidator
    {
        public IReadOnlyList<string> Validate(JsonElement schema, JsonElement instance, long maxSizeBytes) => [];
    }

    private sealed class StubCapability : IConversationCapability
    {
        public Action? OnExecute { get; set; }

        public string? SucceedWith { get; set; } = "{}";

        public string? FailWith { get; set; }

        public string Name => "stub";

        public string Description => "A stub capability.";

        public string WhenToUse => "Use when a test needs a capability to exist.";

        public string ArgumentHint => "anything";

        public string UsageGuidance => "Report what came back.";

        public string Label => "Stub";

        public string OfferDescription => "A stub.";

        public string AcknowledgementTemplate => "OK, doing the thing.";

        public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

        public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

        public string InputSchemaJson => """{"type":"object"}""";

        public string OutputSchemaJson => """{"type":"object"}""";

        public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

        public bool IsAvailable(TurnContext context) => true;

        public Task<AgentToolResult> ExecuteAsync(
            AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
        {
            OnExecute?.Invoke();

            return Task.FromResult(FailWith is not null
                ? AgentToolResult.Failure(FailWith)
                : AgentToolResult.Success(JsonDocument.Parse(SucceedWith ?? "{}")));
        }
    }
}
