using System.Reflection;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Application.Authentication;
using AskLucy.Application.Behaviors;
using AskLucy.Application.Documents.Commands;
using AskLucy.Application.Documents.Processing;
using AskLucy.Application.Documents.Processing.Stages;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.Mcp.Resilience;
using AskLucy.Application.Mcp.Tools;
using AskLucy.Application.Mcp.Validation;
using AskLucy.Application.Memory;
using AskLucy.Application.Options;
using AskLucy.Application.Retrieval;
using AskLucy.Application.Retrieval.Indexing;
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

        // AI Memory System (specs/018-ai-memory-system) — Foundational.
        services.AddScoped<IMemoryService, MemoryService>();
        services.AddScoped<IMemoryConflictDetectionService, MemoryConflictDetectionService>();
        // Enqueued via IBackgroundJobClient against the interface (DocumentProcessingPipeline's
        // idiom) — only the interface mapping is needed, unlike the plain recurring sweep/cleanup
        // jobs (Infrastructure) that Hangfire's RecurringJob.AddOrUpdate<T> resolves by concrete type.
        services.AddScoped<IMemoryExtractionJob, MemoryExtractionJob>();
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
