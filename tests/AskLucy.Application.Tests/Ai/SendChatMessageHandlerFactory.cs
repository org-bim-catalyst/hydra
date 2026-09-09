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
using AskLucy.Application.SiteBoundaries;
using AskLucy.Application.Tests.Conversations.Runtime;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AskLucy.Application.Tests.Ai;

/// <summary>
/// Builds a <see cref="SendChatMessageCommandHandler"/> wired to a real
/// <see cref="ConversationTurnOrchestrator"/> over the caller's substitutes, with an
/// <b>empty capability catalog</b> by default.
///
/// <para>
/// <b>Why empty is the right default for these fixtures.</b> The pre-existing
/// <c>SendChatMessage*</c> test files exercise the fast path — RAG, memory, the standing
/// system-prompt stack — none of which changed shape when the orchestrator gained a decide
/// step. With zero registered capabilities, <c>ConversationCapabilityCatalog.BuildIndexAsync</c>
/// returns an empty index, and <c>TurnDecider.DecideAsync</c> short-circuits on that before
/// touching any of its own dependencies (specs/045 FR-006) — so the decide step costs these
/// fixtures nothing and never turns their fast-path scenarios into act-path ones. The decider,
/// executor and their dependencies below exist only to satisfy the constructor; none is called
/// in this configuration.
/// </para>
///
/// <para>
/// Tests that need the act path — beats, narration, capability dispatch — register real
/// capabilities via <see cref="Create"/>'s <paramref name="capabilities"/> parameter instead of
/// using this default.
/// </para>
/// </summary>
internal static class SendChatMessageHandlerFactory
{
    public static SendChatMessageCommandHandler Create(
        IAIProviderResolver resolver,
        IAIProviderRepository providers,
        IAIModelRepository models,
        IConversationKnowledgeBaseRepository conversationKnowledgeBases,
        IRagService ragService,
        IMemoryService memoryService,
        ILocationResolutionService locationResolutionService,
        IBoundaryResolutionService boundaryResolutionService,
        IUserChatRepository userChatRepository,
        ICurrentUserAccessor currentUser,
        IBackgroundJobClient backgroundJobClient,
        IOptions<LocationResolutionOptions> locationResolutionOptions,
        IOptions<BoundaryScoringOptions> boundaryScoringOptions,
        ILogger logger,
        SendChatMessageCommandValidator validator,
        IEnumerable<IAgentTool>? capabilities = null)
    {
        // The four parameters above that the new orchestrator no longer takes
        // (locationResolutionService, boundaryResolutionService, locationResolutionOptions,
        // boundaryScoringOptions) are kept on this factory's own
        // signature rather than removed, so every existing call site — written when the
        // orchestrator still took them — keeps compiling unchanged. They are simply unused here;
        // that behaviour now lives inside ResolveLocationCapability/ResolveSiteBoundaryCapability/
        // AdjustViewerFocusCapability, which a test opts into via `capabilities` when it actually
        // wants the act path. ViewerZoomDetector itself was deleted outright (T037): it was a
        // pure keyword matcher with no external dependency and no unresolved design question
        // blocking its removal, unlike the location classifier this factory still accepts.
        _ = locationResolutionService;
        _ = boundaryResolutionService;
        _ = locationResolutionOptions;
        _ = boundaryScoringOptions;

        var toolCatalog = new AgentToolCatalog(capabilities ?? [], new EmptyMcpToolRegistry());
        var runtimeOptions = Microsoft.Extensions.Options.Options.Create(new ConversationRuntimeOptions());

        var embeddingService = Substitute.For<IEmbeddingService>();
        var indexRetriever = new CapabilityIndexRetriever(
            embeddingService, runtimeOptions, NullLogger<CapabilityIndexRetriever>.Instance);
        var capabilityCatalog = new ConversationCapabilityCatalog(toolCatalog, indexRetriever, runtimeOptions);

        var decisionParser = new TurnDecisionParser(runtimeOptions);
        // Real dependencies, but unreachable whenever the index is empty (the default here) —
        // TurnDecider checks that before touching any of them. A test that registers capabilities
        // and therefore exercises the decide call for real should build its own TurnDecider
        // against providers/models it actually configured.
        var turnDecider = new TurnDecider(
            new AiCapabilityProviderResolver(
                Substitute.For<IAiCapabilityAssignmentRepository>(), providers, models,
                new DefaultProviderResolver(Substitute.For<IAIProviderRepository>(), Substitute.For<IAIModelRepository>()),
                NullLogger<AiCapabilityProviderResolver>.Instance),
            providers, models, resolver, decisionParser, NullLogger<TurnDecider>.Instance);

        var capabilityExecutor = new CapabilityExecutor(
            new AgentPolicyEvaluator(Substitute.For<IAgentPolicyRepository>()),
            Substitute.For<IJsonSchemaValidator>(),
            runtimeOptions,
            NullLogger<CapabilityExecutor>.Instance);

        // specs/045 US2 — same "unreachable when nothing is offerable" story as turnDecider above:
        // with the default empty catalog every intent either answers (suppressed outright) or has
        // nothing offerable, so OfferSuppressionRules never lets this run. A test that registers
        // capabilities and wants the offer step for real builds its own against providers/models
        // it actually configured, same convention as turnDecider.
        var offerGenerator = new SuggestedActionOfferGenerator(
            new AiCapabilityProviderResolver(
                Substitute.For<IAiCapabilityAssignmentRepository>(), providers, models,
                new DefaultProviderResolver(Substitute.For<IAIProviderRepository>(), Substitute.For<IAIModelRepository>()),
                NullLogger<AiCapabilityProviderResolver>.Instance),
            providers, models, resolver, capabilityCatalog,
            new SuggestedActionGrounder(Substitute.For<IJsonSchemaValidator>()),
            runtimeOptions, NullLogger<SuggestedActionOfferGenerator>.Instance);

        // specs/045 Phase 6 — no flows registered by default, the same "empty by default" story
        // as the capability catalog above: with nothing registered, DecideAsync's own flowIndex
        // is empty and every flow-run branch in the orchestrator is simply never reached.
        var flowCatalog = new ConversationFlowCatalog([]);
        var narrator = new CapabilityNarrator(NullLogger<CapabilityNarrator>.Instance);
        var flowRunner = new FlowRunner(capabilityCatalog, capabilityExecutor, narrator, runtimeOptions, NullLogger<FlowRunner>.Instance);
        var subAgentDelegator = new SubAgentDelegator(
            TestServiceScopeFactory.Create(capabilityCatalog, capabilityExecutor),
            capabilityCatalog, narrator, runtimeOptions, NullLogger<SubAgentDelegator>.Instance);
        var turnRecorder = new TurnRecorder(
            Substitute.For<IAgentRepository>(), Substitute.For<IAgentExecutionRepository>(), Substitute.For<IUnitOfWork>(),
            NullLogger<TurnRecorder>.Instance);

        var orchestratorLogger = logger as ILogger<ConversationTurnOrchestrator>
            ?? new CategoryAdapter(logger);

        var orchestrator = new ConversationTurnOrchestrator(
            conversationKnowledgeBases,
            ragService,
            memoryService,
            userChatRepository,
            currentUser,
            backgroundJobClient,
            capabilityCatalog,
            flowCatalog,
            turnDecider,
            capabilityExecutor,
            flowRunner,
            subAgentDelegator,
            offerGenerator,
            narrator,
            turnRecorder,
            orchestratorLogger);

        return new SendChatMessageCommandHandler(resolver, providers, models, orchestrator, validator);
    }

    /// <summary>Forwards every call to the test's own <see cref="ILogger"/> substitute, so <c>Received().Log(...)</c> assertions keep observing the orchestrator's messages.</summary>
    private sealed class CategoryAdapter(ILogger inner) : ILogger<ConversationTurnOrchestrator>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(logLevel, eventId, state, exception, formatter);
    }

    /// <summary>No MCP servers in any of these fixtures — a real empty implementation rather than a substitute, since nothing here needs to configure its behaviour.</summary>
    private sealed class EmptyMcpToolRegistry : IMcpToolRegistry
    {
        public IReadOnlyCollection<IAgentTool> ActiveTools => [];

        public Task InvalidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public static ILogger NullLogger => NullLogger<ConversationTurnOrchestrator>.Instance;
}
