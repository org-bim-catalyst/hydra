using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;
using AskLucy.Application.Conversations.Runtime;
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
/// specs/045 T119, contracts/turn-stream.md §7 (constitution §2.VIII, FR-039-FR-042) — one test
/// per failure-matrix row that is meaningfully exercisable through the real
/// <see cref="ConversationTurnOrchestrator"/>, asserting the user-visible text or behaviour the
/// contract names.
/// <para>
/// Rows already owned by another test file's own dedicated coverage are not duplicated here:
/// "slice's capability key ungrounded" / "all slices dropped" (<c>TurnDecisionParserTests</c>),
/// "grounded offer fails validation" (<c>SuggestedActionGrounderTests</c>), "offer step fails"
/// (<c>SuggestedActionOfferGenerator</c>'s own "never throws" contract — nothing at the
/// orchestrator level to add once the concrete class already guarantees it), "selection names a
/// stale/unavailable row" (<c>SelectedActionDispatchTests</c>), and the capability-budget-timeout
/// mechanics themselves (<c>CapabilityExecutorTests</c>). What's asserted here is specifically the
/// end-to-end, user-visible shape those mechanisms produce once wired into a real turn.
/// </para>
/// </summary>
public sealed class TurnFailureMatrixTests
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

    public TurnFailureMatrixTests()
    {
        _knowledgeBases.GetByConversationAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AskLucy.Domain.Retrieval.ConversationKnowledgeBase>());
        _memoryService.RetrieveRelevantMemoriesAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryRetrievalOutcome(MemoryRetrievalOutcomeType.NoneRelevant, null, [], null));
        _userChatRepository.GetByIdAsync(_chatId, Arg.Any<CancellationToken>()).Returns((UserChat?)null);
        _currentUser.UserId.Returns("user-1");
    }

    private ConversationTurnOrchestrator BuildOrchestrator(IConversationCapability? capability = null, int? briefTimeoutSeconds = null, int? maxTurnDurationSeconds = null)
    {
        var toolCatalog = new AgentToolCatalog(capability is null ? [] : [capability], new EmptyMcpToolRegistry());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions
        {
            BriefCapabilityTimeoutSeconds = briefTimeoutSeconds ?? 10,
            MaxTurnDurationSeconds = maxTurnDurationSeconds ?? 90,
        });
        var indexRetriever = new CapabilityIndexRetriever(Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance);
        var capabilityCatalog = new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions);
        var capabilityExecutor = new CapabilityExecutor(
            new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()), new PermissiveSchemaValidator(), runtimeOptions, NullLogger<CapabilityExecutor>.Instance);
        var flowCatalog = new ConversationFlowCatalog([]);
        var narrator = new CapabilityNarrator(NullLogger<CapabilityNarrator>.Instance);
        var flowRunner = new FlowRunner(capabilityCatalog, capabilityExecutor, narrator, runtimeOptions, NullLogger<FlowRunner>.Instance);
        var subAgentDelegator = new SubAgentDelegator(
            TestServiceScopeFactory.Create(capabilityCatalog, capabilityExecutor), capabilityCatalog, narrator, runtimeOptions, NullLogger<SubAgentDelegator>.Instance);
        var turnRecorder = new TurnRecorder(
            Substitute.For<IAgentRepository>(), Substitute.For<IAgentExecutionRepository>(), Substitute.For<IUnitOfWork>(), NullLogger<TurnRecorder>.Instance);

        return new ConversationTurnOrchestrator(
            _knowledgeBases, _ragService, _memoryService, _userChatRepository, _currentUser,
            _backgroundJobClient, capabilityCatalog, flowCatalog, _decider, capabilityExecutor, flowRunner, subAgentDelegator, _offerGenerator,
            narrator, turnRecorder, NullLogger<ConversationTurnOrchestrator>.Instance);
    }

    private ConversationTurnRequest Request(string message) => new(_chatId, [new ChatMessageDto("user", message)], _provider, "test-model", GenerationParameters: null);

    private static async Task<List<ChatStreamChunk>> CollectAsync(ConversationTurnOrchestrator orchestrator, ConversationTurnRequest request, CancellationToken cancellationToken = default)
    {
        var chunks = new List<ChatStreamChunk>();
        await foreach (var chunk in orchestrator.RunAsync(request, cancellationToken))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    /// <summary>Failure matrix row 1 — decision call throws / unparseable after retry.</summary>
    [Fact]
    public async Task DecisionCallDegrades_ShouldAnswerNormally_WithAVisibleExplanationAppended()
    {
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(TurnDecision.Degraded);
        _provider.StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable([new StreamChunk("Here is a direct answer.")]));

        var chunks = await CollectAsync(BuildOrchestrator(), Request("do something ambiguous"));

        var content = string.Concat(chunks.Select(c => c.ContentDelta));
        content.Should().Contain("Here is a direct answer.");
        content.Should().Contain("I couldn't work out a plan for that, so here's a direct answer.");
    }

    /// <summary>Contrasted with the row above — a genuinely ordinary answer never gets the degradation sentence.</summary>
    [Fact]
    public async Task OrdinaryAnswer_ShouldNeverGetTheDegradationExplanation()
    {
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(TurnDecision.AnswerOnly);
        _provider.StreamChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(ToAsyncEnumerable([new StreamChunk("Sure, here you go.")]));

        var chunks = await CollectAsync(BuildOrchestrator(), Request("what's the weather like generally"));

        var content = string.Concat(chunks.Select(c => c.ContentDelta));
        content.Should().NotContain("I couldn't work out a plan");
    }

    /// <summary>Failure matrix row 6 — capability throws, caught by the isolation wrapper.</summary>
    [Fact]
    public async Task CapabilityThrows_ShouldBeIsolated_AndNarrateAFailureRatherThanCrashTheTurn()
    {
        var capability = new StubCapability { ThrowOnExecute = new InvalidOperationException("boom") };
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Act, [new TurnSlice("stub", "{}", null, null)]));
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("It failed unexpectedly.", new ChatUsage(null, null, null, null, null)));

        var chunks = await CollectAsync(BuildOrchestrator(capability), Request("do the thing"));

        var content = string.Concat(chunks.Select(c => c.ContentDelta));
        content.Should().Contain("It failed unexpectedly.");
    }

    /// <summary>Failure matrix row 7 — capability exceeds its budget; recorded and narrated as a timeout, never a crash.</summary>
    [Fact]
    public async Task CapabilityExceedsItsBudget_ShouldReportATimeout_NotACrash()
    {
        var capability = new StubCapability { HangUntilCancelled = true };
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Act, [new TurnSlice("stub", "{}", null, null)]));
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(new ChatCompletionResult("That took longer than expected.", new ChatUsage(null, null, null, null, null)));

        var chunks = await CollectAsync(BuildOrchestrator(capability, briefTimeoutSeconds: 1), Request("do the slow thing"));

        var content = string.Concat(chunks.Select(c => c.ContentDelta));
        content.Should().Contain("That took longer than expected.");
    }

    /// <summary>
    /// Failure matrix row 8 — the turn's own overall budget, not one capability's. specs/045
    /// T116 — SubAgentDelegator's own wave loop stop, mirroring FlowRunner's identical one.
    /// </summary>
    [Fact]
    public async Task TurnExceedsItsOwnDuration_ShouldStopWithAnExplicitStatement_KeepingWhatAlreadyCompleted()
    {
        var first = new StubCapability { Key = "first", SucceedWith = """{"status":"done"}""" };
        var second = new StubCapability { Key = "second", SucceedWith = """{"status":"done"}""" };
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Act,
                [new TurnSlice("first", "{}", null, DependsOn: null), new TurnSlice("second", "{}", null, DependsOn: 0)]));
        _provider.ChatAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<string>(), Arg.Any<GenerationParametersDto?>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                // The first wave's own narration call is what spends the (tiny) turn budget, so
                // the second wave — "second" depends on "first" and cannot start until it
                // finishes — finds the clock already past it.
                await Task.Delay(TimeSpan.FromMilliseconds(50));
                return new ChatCompletionResult("Done.", new ChatUsage(null, null, null, null, null));
            });

        var chunks = await CollectAsync(BuildTwoCapabilityOrchestrator(first, second, maxTurnDurationSeconds: 0), Request("do two things"));

        var content = string.Concat(chunks.Select(c => c.ContentDelta));
        content.Should().Contain("I've stopped there");
        content.Should().Contain("Done.", "the first (already-completed) delegation's own real result is kept, not discarded");
    }

    /// <summary>Failure matrix row 12 — client disconnects; the original token's cancellation must propagate, never be swallowed or mis-reported.</summary>
    [Fact]
    public async Task ClientDisconnects_ShouldPropagateCancellation_RatherThanBeingSwallowed()
    {
        var capability = new StubCapability { HangUntilCancelled = true };
        _decider.DecideAsync(Arg.Any<TurnContext>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<IReadOnlyList<CapabilityIndexEntry>>(), Arg.Any<CancellationToken>())
            .Returns(new TurnDecision(TurnIntent.Act, [new TurnSlice("stub", "{}", null, null)]));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await CollectAsync(BuildOrchestrator(capability, briefTimeoutSeconds: 30), Request("do the thing"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private ConversationTurnOrchestrator BuildTwoCapabilityOrchestrator(IConversationCapability first, IConversationCapability second, int maxTurnDurationSeconds)
    {
        var toolCatalog = new AgentToolCatalog([first, second], new EmptyMcpToolRegistry());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions { MaxTurnDurationSeconds = maxTurnDurationSeconds });
        var indexRetriever = new CapabilityIndexRetriever(Substitute.For<IEmbeddingService>(), runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance);
        var capabilityCatalog = new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions);
        var capabilityExecutor = new CapabilityExecutor(
            new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()), new PermissiveSchemaValidator(), runtimeOptions, NullLogger<CapabilityExecutor>.Instance);
        var flowCatalog = new ConversationFlowCatalog([]);
        var narrator = new CapabilityNarrator(NullLogger<CapabilityNarrator>.Instance);
        var flowRunner = new FlowRunner(capabilityCatalog, capabilityExecutor, narrator, runtimeOptions, NullLogger<FlowRunner>.Instance);
        var subAgentDelegator = new SubAgentDelegator(
            TestServiceScopeFactory.Create(capabilityCatalog, capabilityExecutor), capabilityCatalog, narrator, runtimeOptions, NullLogger<SubAgentDelegator>.Instance);
        var turnRecorder = new TurnRecorder(
            Substitute.For<IAgentRepository>(), Substitute.For<IAgentExecutionRepository>(), Substitute.For<IUnitOfWork>(), NullLogger<TurnRecorder>.Instance);

        return new ConversationTurnOrchestrator(
            _knowledgeBases, _ragService, _memoryService, _userChatRepository, _currentUser,
            _backgroundJobClient, capabilityCatalog, flowCatalog, _decider, capabilityExecutor, flowRunner, subAgentDelegator, _offerGenerator,
            narrator, turnRecorder, NullLogger<ConversationTurnOrchestrator>.Instance);
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

    private sealed class PermissiveSchemaValidator : IJsonSchemaValidator
    {
        public IReadOnlyList<string> Validate(JsonElement schema, JsonElement instance, long maxSizeBytes) => [];
    }

    /// <summary>A capability whose behaviour each test dials in.</summary>
    private sealed class StubCapability : IConversationCapability
    {
        public string Key { get; init; } = "stub";

        public string? SucceedWith { get; set; } = "{}";

        public string? FailWith { get; set; }

        public Exception? ThrowOnExecute { get; init; }

        public bool HangUntilCancelled { get; init; }

        public string Name => Key;

        public string Description => "A stub capability.";

        public string WhenToUse => "Use when a test needs a capability to exist.";

        public string ArgumentHint => "anything";

        public string UsageGuidance => "Report what came back.";

        public string Label => Key;

        public string OfferDescription => "A stub.";

        public string AcknowledgementTemplate => "OK, doing the thing.";

        public AgentToolRiskLevel RiskLevel => AgentToolRiskLevel.Low;

        public IReadOnlyList<AgentToolPermission> RequiredPermissions => [];

        public string InputSchemaJson => """{"type":"object"}""";

        public string OutputSchemaJson => """{"type":"object"}""";

        public CapabilityDuration ExpectedDuration => CapabilityDuration.Brief;

        public SubAgentArea Area => SubAgentArea.Location;

        public bool IsAvailable(TurnContext context) => true;

        public async Task<AgentToolResult> ExecuteAsync(AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
        {
            if (ThrowOnExecute is not null)
            {
                throw ThrowOnExecute;
            }

            if (HangUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return FailWith is not null
                ? AgentToolResult.Failure(FailWith)
                : AgentToolResult.Success(JsonDocument.Parse(SucceedWith ?? "{}"));
        }
    }
}
