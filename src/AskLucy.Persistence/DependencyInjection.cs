using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Persistence.Identity;
using AskLucy.Persistence.Interceptors;
using AskLucy.Persistence.Repositories;
using AskLucy.Persistence.Retrieval;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AskLucy.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditSaveChangesInterceptor>();

        // Resolve the connection string lazily from the container's IConfiguration at
        // DbContext-construction time, not eagerly from the `configuration` parameter
        // captured here — the latter can be a snapshot taken before all configuration
        // sources (e.g. a test host's overrides) have been layered in.
        services.AddDbContext<AskLucyDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>()
                .GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            options.UseSqlServer(connectionString)
                   .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                // Three, not five: the sign-in page now names the lockout explicitly and offers a
                // way out (contact support / reset the password), so a tighter threshold no longer
                // leaves a locked-out user guessing what happened.
                options.Lockout.MaxFailedAccessAttempts = 3;
            })
            .AddRoles<ApplicationRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddEntityFrameworkStores<AskLucyDbContext>();

        services.AddAutoMapper(cfg => { }, typeof(DependencyInjection).Assembly);

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ISystemAccountProvisioner, SystemAccountProvisioner>();
        // Registered here, and before AddInfrastructure's own provisioning hosted services are
        // added, so the .NET Generic Host starts and fully awaits this one first — see
        // SystemAccountProvisioningHostedService's own doc comment for why this eliminates the
        // system-account creation race rather than merely catching its consequence.
        services.AddHostedService<SystemAccountProvisioningHostedService>();
        services.AddScoped<IUserChatRepository, UserChatRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IUserCookieConsentRepository, UserCookieConsentRepository>();
        services.AddScoped<IUserAdminRepository, UserAdminRepository>();
        services.AddScoped<IAdminDashboardRepository, AdminDashboardRepository>();
        services.AddScoped<IIdentityService, IdentityService>();

        // Role Definition, Role Assignment & Permission Catalogue (specs/055-role-management) — Foundational.
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRoleAssignmentRepository, RoleAssignmentRepository>();
        services.AddScoped<IRoleAuditLogRepository, RoleAuditLogRepository>();
        services.AddScoped<IEffectivePermissionResolver, AskLucy.Application.Authorization.EffectivePermissionResolver>();
        services.AddScoped<IAIProviderRepository, AIProviderRepository>();
        services.AddScoped<IAiCapabilityAssignmentRepository, AiCapabilityAssignmentRepository>();
        services.AddScoped<IAIModelRepository, AIModelRepository>();
        services.AddScoped<IProviderHealthCheckRepository, ProviderHealthCheckRepository>();
        services.AddScoped<IUserAiPreferenceRepository, UserAiPreferenceRepository>();
        services.AddScoped<IUserVoicePreferenceRepository, UserVoicePreferenceRepository>();
        services.AddScoped<IUserPanelPreferenceRepository, UserPanelPreferenceRepository>();
        services.AddScoped<IVoiceProviderFailoverEventRepository, VoiceProviderFailoverEventRepository>();
        services.AddScoped<IVoiceProviderRepository, VoiceProviderRepository>();
        services.AddScoped<ICustomModelRepository, CustomModelRepository>();
        services.AddScoped<IKnowledgeBaseRepository, KnowledgeBaseRepository>();
        services.AddScoped<IKnowledgeBaseAuditLogRepository, KnowledgeBaseAuditLogRepository>();
        services.AddScoped<IKnowledgeBaseDocumentRepository, KnowledgeBaseDocumentRepository>();
        services.AddScoped<IKnowledgeBaseFolderRepository, KnowledgeBaseFolderRepository>();
        services.AddScoped<IKnowledgeBaseCategoryRepository, KnowledgeBaseCategoryRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentFolderRepository, DocumentFolderRepository>();
        services.AddScoped<IDocumentProcessingJobRepository, DocumentProcessingJobRepository>();
        services.AddScoped<IDocumentNotificationRepository, DocumentNotificationRepository>();
        services.AddScoped<IDocumentUploadSessionRepository, DocumentUploadSessionRepository>();
        services.AddScoped<IDocumentStatisticsRepository, DocumentStatisticsRepository>();

        // Retrieval (specs/016-rag-semantic-search) — Foundational.
        services.AddScoped<IDocumentChunkRepository, DocumentChunkRepository>();
        services.AddScoped<IEmbeddingRepository, EmbeddingRepository>();
        services.AddScoped<IEmbeddingProviderRepository, EmbeddingProviderRepository>();
        services.AddScoped<IIndexingJobRepository, IndexingJobRepository>();
        services.AddScoped<IConversationKnowledgeBaseRepository, ConversationKnowledgeBaseRepository>();
        services.AddScoped<IVectorStore, SqlServerVectorStore>();
        services.AddScoped<IKeywordSearchService, FullTextKeywordSearch>();

        // AI Memory System (specs/018-ai-memory-system) — Foundational.
        services.AddScoped<IMemoryRepository, MemoryRepository>();
        services.AddScoped<IMemoryVersionRepository, MemoryVersionRepository>();
        services.AddScoped<IMemoryApprovalRepository, MemoryApprovalRepository>();
        services.AddScoped<IMemoryConflictRepository, MemoryConflictRepository>();
        services.AddScoped<IMemoryEmbeddingRepository, MemoryEmbeddingRepository>();
        services.AddScoped<IMemoryAuditLogRepository, MemoryAuditLogRepository>();
        services.AddScoped<IMemoryNotificationRepository, MemoryNotificationRepository>();
        services.AddScoped<IMemoryPreferenceRepository, MemoryPreferenceRepository>();
        services.AddScoped<IMemoryReferenceRepository, MemoryReferenceRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IMemoryExportJobRepository, MemoryExportJobRepository>();
        services.AddScoped<IMemoryVectorStore, AskLucy.Persistence.Memory.SqlServerMemoryVectorStore>();

        // Prompt Library & Prompt Engineering Workspace (specs/019-prompt-library-workspace) — Foundational.
        services.AddScoped<IPromptRepository, PromptRepository>();
        services.AddScoped<IPromptFolderRepository, PromptFolderRepository>();
        services.AddScoped<IPromptCategoryRepository, PromptCategoryRepository>();
        services.AddScoped<IPromptTestCaseRepository, PromptTestCaseRepository>();
        services.AddScoped<IPromptExecutionRepository, PromptExecutionRepository>();
        services.AddScoped<IPromptAuditLogRepository, PromptAuditLogRepository>();

        // AI Agent Framework & Agent Runtime (specs/020-ai-agent-framework) — Foundational.
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IDatabaseMigrationStatus, DatabaseMigrationStatus>();
        services.AddScoped<IAgentExecutionRepository, AgentExecutionRepository>();
        services.AddScoped<IAgentPolicyRepository, AgentPolicyRepository>();
        services.AddScoped<IAgentAuditLogRepository, AgentAuditLogRepository>();

        // MCP Integration (specs/021-mcp-integration) — Foundational.
        services.AddScoped<IMcpServerRepository, McpServerRepository>();
        services.AddScoped<IMcpToolRepository, McpToolRepository>();
        services.AddScoped<IMcpResourceRepository, McpResourceRepository>();
        services.AddScoped<IMcpPromptRepository, McpPromptRepository>();
        services.AddScoped<IMcpAuditLogRepository, McpAuditLogRepository>();

        // Workflow & Tool Orchestration Engine (specs/022-workflow-orchestration-engine) — Foundational.
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddScoped<IWorkflowExecutionRepository, WorkflowExecutionRepository>();
        services.AddScoped<IWorkflowPolicyRepository, WorkflowPolicyRepository>();
        services.AddScoped<IWorkflowAuditLogRepository, WorkflowAuditLogRepository>();

        // Site Analysis Agent (specs/057-site-analysis-agent).
        services.AddScoped<ISiteAnalysisRepository, SiteAnalysisRepository>();

        return services;
    }
}
