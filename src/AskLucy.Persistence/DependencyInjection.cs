using AskLucy.Application.Abstractions;
using AskLucy.Persistence.Identity;
using AskLucy.Persistence.Interceptors;
using AskLucy.Persistence.Repositories;
using AskLucy.Persistence.Retrieval;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole>()
            .AddSignInManager()
            .AddDefaultTokenProviders()
            .AddEntityFrameworkStores<AskLucyDbContext>();

        services.AddAutoMapper(cfg => { }, typeof(DependencyInjection).Assembly);

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserChatRepository, UserChatRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IUserCookieConsentRepository, UserCookieConsentRepository>();
        services.AddScoped<IUserAdminRepository, UserAdminRepository>();
        services.AddScoped<IAdminDashboardRepository, AdminDashboardRepository>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IAIProviderRepository, AIProviderRepository>();
        services.AddScoped<IAIModelRepository, AIModelRepository>();
        services.AddScoped<IProviderHealthCheckRepository, ProviderHealthCheckRepository>();
        services.AddScoped<IUserAiPreferenceRepository, UserAiPreferenceRepository>();
        services.AddScoped<IUserVoicePreferenceRepository, UserVoicePreferenceRepository>();
        services.AddScoped<IVoiceProviderFailoverEventRepository, VoiceProviderFailoverEventRepository>();
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
        services.AddScoped<IAgentExecutionRepository, AgentExecutionRepository>();
        services.AddScoped<IAgentPolicyRepository, AgentPolicyRepository>();
        services.AddScoped<IAgentAuditLogRepository, AgentAuditLogRepository>();

        return services;
    }
}
