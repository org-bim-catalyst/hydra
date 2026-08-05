using AskLucy.Application.Abstractions;
using AskLucy.Persistence.Identity;
using AskLucy.Persistence.Interceptors;
using AskLucy.Persistence.Repositories;
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

        return services;
    }
}
