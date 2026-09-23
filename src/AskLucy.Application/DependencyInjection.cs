using System.Reflection;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Ai.Images;
using AskLucy.Application.Authentication;
using AskLucy.Application.Authentication.PasswordReset;
using AskLucy.Application.Behaviors;
using AskLucy.Application.Conversations.Capabilities;
using AskLucy.Application.Conversations.Flows;
using AskLucy.Application.Conversations.Runtime;
using AskLucy.Application.Documents.Commands;
using AskLucy.Application.Documents.Processing;
using AskLucy.Application.Documents.Processing.Stages;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.Locations;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Mcp.Validation;
using AskLucy.Application.Memory;
using AskLucy.Application.Options;
using AskLucy.Application.Retrieval;
using AskLucy.Application.Retrieval.Indexing;
using AskLucy.Application.SiteAnalysis;
using AskLucy.Application.SiteAnalysis.Tools;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Application.Workflows.Runtime;
using AskLucy.Application.Workflows.Validation;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Application;

public static class DependencyInjection
{
    /// <summary><paramref name="isNonProductionEnvironment"/> gates dev/test-only registrations (e.g. <see cref="Agents.Tools.FakeHighRiskTool"/>, spec.md User Story 3 T105) — a plain <see cref="bool"/>, not <c>IHostEnvironment</c>, to avoid adding a hosting-abstraction package reference to this layer purely for one conditional (constitution §2.III, mirrors <c>AskLucy.Infrastructure.DependencyInjection</c>'s <c>environment.IsDevelopment()</c> gate for <c>ConsoleEmailSender</c>, but expressed at the call site instead).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration, bool isNonProductionEnvironment = false)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(config => config.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        services.AddScoped<TokenIssuer>();
        services.AddScoped<DefaultProviderResolver>();
        services.AddScoped<AiCapabilityProviderResolver>();

        // specs/070: every voice-output handler speaks through the router, which orders the
        // Infrastructure-registered ITextToSpeechEngine set by the admin-configured VoiceProvider
        // rows. Scoped, so an engine that fails is skipped for the rest of the request only.
        services.AddScoped<ITextToSpeechProvider, VoiceProviderRouter>();

        // Image generation (specs/057 follow-up) — one service for every caller; the model comes
        // from the ImageGeneration capability assignment, and any provider response form (URL,
        // base64, data URL, binary) is normalised by the materializer.
        services.AddScoped<GeneratedImageMaterializer>();
        services.AddScoped<IImageGenerationService, ImageGenerationService>();
        services.AddScoped<IDocumentProcessingPipeline, DocumentProcessingPipeline>();
        services.AddScoped<DocumentUploadFinalizer>();
        services.AddScoped<IProcessingStageHandler, ValidationStageHandler>();
        services.AddScoped<IProcessingStageHandler, OcrStageHandler>();
        services.AddScoped<IProcessingStageHandler, TextExtractionStageHandler>();
        services.AddScoped<IProcessingStageHandler, MetadataExtractionStageHandler>();
        services.AddScoped<IProcessingStageHandler, ClassificationStageHandler>();
        services.AddScoped<IProcessingStageHandler, LanguageDetectionStageHandler>();
        services.AddScoped<IProcessingStageHandler, PreviewGenerationStageHandler>();

        // Retrieval (specs/016-rag-semantic-search) — Foundational.
        services.AddScoped<IIndexingOrchestrator, IndexingOrchestrator>();
        services.AddScoped<SearchResultEnricher>();
        // User Story 1 ("Chat with your documents and get cited answers").
        services.AddScoped<IRagService, RagService>();

        // Location Query Resolution (specs/037-location-query-resolution).
        // Registered as the concrete type as well: ScopeIsolatedLocationResolutionService
        // resolves it from a fresh scope so the concurrent location task never shares the
        // request DbContext with the reply stream (see that class for the production race).
        services.AddScoped<LocationResolutionService>();
        services.AddScoped<ILocationResolutionService, ScopeIsolatedLocationResolutionService>();
        services.AddOptions<LocationResolutionOptions>()
            .BindConfiguration(LocationResolutionOptions.SectionName)
            .ValidateOnStart();

        // Site Boundary Resolution (specs/042-site-boundary-resolution) — extends Locations
        // rather than duplicating it (research.md #1); the primary invocation path is a
        // SendChatMessageCommandHandler pipeline hook (research.md #11), not an IAgentTool.
        services.AddScoped<BoundaryCandidateScorer>();
        services.AddScoped<IBoundaryResolutionService, BoundaryResolutionService>();
        services.AddOptions<BoundaryScoringOptions>()
            .BindConfiguration(BoundaryScoringOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // AI Memory System (specs/018-ai-memory-system) — Foundational.
        services.AddScoped<IMemoryService, MemoryService>();
        services.AddScoped<IMemoryConflictDetectionService, MemoryConflictDetectionService>();
        // Enqueued via IBackgroundJobClient against the interface (DocumentProcessingPipeline's
        // idiom) — only the interface mapping is needed, unlike the plain recurring sweep/cleanup
        // jobs (Infrastructure) that Hangfire's RecurringJob.AddOrUpdate<T> resolves by concrete type.
        services.AddScoped<IMemoryExtractionJob, MemoryExtractionJob>();

        // Password reset issuance runs on a worker so the request path costs the same for every
        // address (specs/058-password-recovery, FR-003).
        services.AddScoped<IPasswordResetIssuanceJob, PasswordResetIssuanceJob>();
        services.AddScoped<IMemoryExportGenerationJob, MemoryExportGenerationJob>();

        // IMemoryCache's concrete registration (AddMemoryCache()) lives in Infrastructure's
        // composition root, not here — Application depends only on the IMemoryCache interface
        // (Microsoft.Extensions.Caching.Abstractions), never the concrete MemoryCache
        // implementation (constitution §3 Dependency Rule).
        services.AddSingleton<KnowledgeBaseDashboardSummaryCache>();

        services.AddOptions<AppOptions>()
            .Bind(configuration.GetSection(AppOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<KnowledgeBaseFolderOptions>()
            .Bind(configuration.GetSection(KnowledgeBaseFolderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<KnowledgeBaseDocumentOptions>()
            .Bind(configuration.GetSection(KnowledgeBaseDocumentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DocumentUploadOptions>()
            .Bind(configuration.GetSection(DocumentUploadOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PromptFolderOptions>()
            .Bind(configuration.GetSection(PromptFolderOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DocumentStorageQuotaOptions>()
            .Bind(configuration.GetSection(DocumentStorageQuotaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // AI Agent Framework & Agent Runtime (specs/020-ai-agent-framework) — Foundational.
        // Individual IAgentTool implementations register themselves as each is added (US2+);
        // an empty IEnumerable<IAgentTool> is a valid state for the catalog itself.
        services.AddScoped<AgentToolCatalog>();

        // User Story 1 ("Create and Run a Simple Agent"). AgentExecutionOrchestrator/
        // AgentExecutionRunner live in Application (not Infrastructure) mirroring
        // IDocumentProcessingPipeline/DocumentProcessingPipeline's precedent — the Hangfire
        // entry point for a multi-step orchestration is itself Application-layer logic, not an
        // Infrastructure concern, since Hangfire's IBackgroundJobClient is already referenced
        // directly from Application elsewhere (SendChatMessageCommandHandler).
        services.AddScoped<IAgentPlanner, AgentPlanner>();
        services.AddScoped<AgentExecutionOrchestrator>();
        services.AddScoped<IAgentExecutionRunner, AgentExecutionRunner>();

        // User Story 2 ("Multi-Step Task Execution with Tools") — the 8 built-in IAgentTool
        // implementations (contracts/agent-tool-contract.md). Each wraps an existing platform
        // capability through its existing abstraction — no new business logic (FR-024).
        services.AddScoped<IAgentTool, ConversationTool>();
        services.AddScoped<IAgentTool, KnowledgeSearchTool>();
        services.AddScoped<IAgentTool, DocumentSearchTool>();
        services.AddScoped<IAgentTool, MemorySearchTool>();
        services.AddScoped<IAgentTool, MemoryWriteTool>();
        services.AddScoped<IAgentTool, PromptExecutionTool>();
        services.AddScoped<IAgentTool, FileReadTool>();
        services.AddScoped<IAgentTool, FileMetadataTool>();

        // spec 021-mcp-integration User Story 5 (FR-037-FR-040) — one singular built-in tool for
        // every MCP resource (unlike McpToolAdapter, which is one instance per discovered tool);
        // registered here alongside the other native tools since, unlike McpToolAdapter, it is not
        // constructed per-server-per-tool by IMcpToolRegistry.
        services.AddScoped<IAgentTool, McpResourceReadTool>();
        // specs/042-site-boundary-resolution research.md #11 — secondary surface for custom
        // agents; the base chat experience is driven by the SendChatMessageCommandHandler
        // pipeline hook registered above, not this tool.
        services.AddScoped<IAgentTool, SiteBoundaryResolverTool>();
        services.AddScoped<AgentBudgetGuard>();
        services.AddScoped<AgentDuplicateToolCallDetector>();

        // User Story 3 ("Approval for Sensitive Actions").
        services.AddScoped<AgentPolicyEvaluator>();
        if (isNonProductionEnvironment)
        {
            // Test/dev-only fixture (quickstart.md Scenario 3) — never present in a Production
            // catalog, since no real High-risk tool ships in this release (research.md's Initial
            // Tools are all Low/Medium risk).
            services.AddScoped<IAgentTool, FakeHighRiskTool>();
        }

        // specs/057-site-analysis-agent — the one specialist in this release; a further one is a
        // provisioner entry + one more registration here, no other change (FR-031).
        services.AddScoped<SiteAnalysisContentComposer>();
        services.AddScoped<SiteAnalysisResultRelay>();
        // Registered as the concrete type as well: ScopeIsolatedSiteAnalysisResultRelay resolves
        // SiteAnalysisResultRelay fresh from its own DI scope on every call (research.md D2 —
        // concurrent workflow branches share one scoped DbContext), mirroring
        // ScopeIsolatedLocationResolutionService/LocationResolutionService exactly.
        services.AddScoped<ISiteAnalysisResultRelay, ScopeIsolatedSiteAnalysisResultRelay>();
        services.AddScoped<ISiteAnalysisDispatcher, SiteAnalysisDispatcher>();
        // Registered straight against IAgentTool, NOT via the `sp => sp.GetRequiredService<T>()`
        // shape the capabilities below still use. That shape re-enters ServiceProvider.GetService
        // from inside a call-site visit that already holds the runtime resolver's lock; when the
        // service's own graph is deep enough for StackGuard to move the remainder of the
        // resolution onto another thread, the two deadlock and every request resolving
        // IEnumerable<IAgentTool> hangs forever with no exception and no log. Both site-analysis
        // tools carry by far the deepest graphs in this collection, so they must not add a
        // re-entrant hop on top. Neither is ever resolved by its concrete type, so the extra
        // registration bought nothing.
        services.AddScoped<IAgentTool, SiteSchematicImageGenerationTool>();

        services.AddOptions<AgentRuntimeOptions>()
            .Bind(configuration.GetSection(AgentRuntimeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<McpRuntimeOptions>()
            .Bind(configuration.GetSection(McpRuntimeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<WorkflowRuntimeOptions>()
            .Bind(configuration.GetSection(WorkflowRuntimeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // specs/045-conversational-agent-runtime — per-turn bounds for the conversational runtime.
        services.AddOptions<ConversationRuntimeOptions>()
            .Bind(configuration.GetSection(ConversationRuntimeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Scoped, not singleton: the turn reads repositories bound to the request's DbContext.
        // Concurrent sub-agent slices (research.md D12) must therefore resolve their OWN scope
        // rather than sharing this one — a shared request DbContext has already caused hard 500s
        // in this codebase once, on DB-credential providers only.
        services.AddScoped<IConversationTurnOrchestrator, ConversationTurnOrchestrator>();

        // Conversation capabilities (specs/045 FR-012/FR-013). Registered directly against
        // IAgentTool — never `sp => sp.GetRequiredService<T>()` — so AgentToolCatalog (the single
        // discovery point the whole agent runtime already uses) sees them without a second catalog
        // existing, and so a capability with a deep dependency graph can never close a DI cycle
        // back through AgentToolCatalog's own IEnumerable<IAgentTool> the way the self-referential
        // factory shape did (see RequestSiteAnalysisCapability's history: that shape hid a real
        // cycle from startup validation and hung every chat turn at runtime instead of failing
        // loudly at boot). None of these is ever resolved by its concrete type — only through
        // IAgentTool or its own static CapabilityKey constant — so nothing is lost by dropping the
        // paired concrete registration. Adding a capability is one line and nothing else.
        services.AddScoped<IAgentTool, ResolveLocationCapability>();
        services.AddScoped<IAgentTool, ResolveSiteBoundaryCapability>();
        services.AddScoped<IAgentTool, AdjustViewerFocusCapability>();
        services.AddScoped<IAgentTool, SearchKnowledgeBaseCapability>();
        services.AddScoped<IAgentTool, SearchMemoryCapability>();
        services.AddScoped<IAgentTool, PresentPanelContentCapability>();
        services.AddScoped<IAgentTool, OpenLivePanelCapability>();
        services.AddScoped<IAgentTool, LoadViewerContentCapability>();
        services.AddScoped<IAgentTool, OpenSolarAnalysisCapability>();
        // Direct, not via a `sp => sp.GetRequiredService<T>()` factory — see the note on
        // SiteSchematicImageGenerationTool above: this exact registration was the one the runtime
        // deadlocked on.
        services.AddScoped<IAgentTool, RequestSiteAnalysisCapability>();

        services.AddScoped<CapabilityIndexRetriever>();
        services.AddScoped<ConversationCapabilityCatalog>();

        // specs/045 Phase 6 — flows. Registered the same way as capabilities: adding one is these
        // two lines, never a change to the orchestrator (IConversationFlow's own doc comment).
        services.AddScoped<LocateAPlaceFlow>();
        services.AddScoped<IConversationFlow>(sp => sp.GetRequiredService<LocateAPlaceFlow>());
        services.AddScoped<ConversationFlowCatalog>();
        services.AddScoped<FlowRunner>();

        // The decide step. Its model is an AiCapability assignment, never a hardcoded string —
        // an administrator picks something fast and cheap for it exactly as they already do for
        // location-intent classification (constitution §9).
        services.AddScoped<TurnDecisionParser>();
        services.AddScoped<ITurnDecider, TurnDecider>();
        services.AddScoped<CapabilityExecutor>();
        services.AddScoped<CapabilityNarrator>();

        // specs/045 Phase 7 (US5) — sub-agent delegation. Uses IServiceScopeFactory (registered by
        // the host, not here) to give every delegated slice its own scope regardless of how many
        // others run alongside it this turn (research.md D12, the comment on the orchestrator's
        // own registration above).
        services.AddScoped<SubAgentDelegator>();

        // specs/045 T041/T111 — the turn record, unblocked now that Phase 8 provisions a real
        // lucy.orchestrator agent to attribute a turn's AgentExecution to.
        services.AddScoped<TurnRecorder>();

        // The offer step (specs/045 US2) — same AiCapability assignment as the decide step
        // (AiCapability.TurnOrchestration's own doc comment covers both jobs).
        services.AddScoped<SuggestedActionGrounder>();
        services.AddScoped<ISuggestedActionOfferGenerator, SuggestedActionOfferGenerator>();

        // specs/045 US3 — resolves and grounds a selected offer row at dispatch time.
        services.AddScoped<ISelectedActionResolver, SelectedActionResolver>();

        // MCP Integration (specs/021-mcp-integration) — Foundational.
        // IMcpToolRegistry/McpConnectionResiliencePolicy are singletons: the registry's cached
        // McpToolAdapter instances must never hold a Scoped dependency (constitution §3), and the
        // resilience policy's circuit-breaker state must persist across executions, not reset per
        // scope (research.md Decisions 1/11, corrected during implementation — see plan.md).
        services.AddSingleton<IMcpToolRegistry, McpToolRegistry>();
        services.AddSingleton<McpConnectionResiliencePolicy>();
        services.AddSingleton<IJsonSchemaValidator, JsonSchemaValidator>();

        // Workflow & Tool Orchestration Engine (specs/022-workflow-orchestration-engine) — Foundational.
        services.AddSingleton<IWorkflowExpressionEvaluator, WorkflowExpressionEvaluator>();
        services.AddScoped<WorkflowGraphValidator>();
        services.AddScoped<WorkflowNodeExecutorRegistry>();

        // User Story 1 ("Create and Run a Simple Deterministic Workflow"). WorkflowExecutionOrchestrator/
        // WorkflowExecutionRunner live in Application (not Infrastructure), mirroring
        // AgentExecutionOrchestrator/AgentExecutionRunner's precedent exactly (research.md
        // Decision 7) — Hangfire's IBackgroundJobClient is already referenced directly from
        // Application elsewhere.
        services.AddScoped<WorkflowBudgetGuard>();
        services.AddScoped<WorkflowPolicyEvaluator>();
        services.AddScoped<WorkflowExecutionOrchestrator>();
        services.AddScoped<IWorkflowExecutionRunner, WorkflowExecutionRunner>();

        // Individual IWorkflowNodeExecutor implementations register themselves as each is added
        // (research.md Decision 1). Only Parallel/Start/End/HumanApproval/Delay are handled
        // directly by WorkflowExecutionOrchestrator, not via a registered executor — Condition and
        // Merge are ordinary registered executors (see each class's own doc comment for why).
        services.AddScoped<IWorkflowNodeExecutor, TransformNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, RagSearchNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, MemorySearchNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, DocumentProcessingNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, FileOperationNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, McpToolNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, NativeToolNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, PromptNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, AgentNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, ValidationNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, ConditionNodeExecutor>();
        services.AddScoped<IWorkflowNodeExecutor, MergeNodeExecutor>();

        return services;
    }
}
