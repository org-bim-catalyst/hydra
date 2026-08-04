using System.Reflection;
using AskLucy.Application.Ai;
using AskLucy.Application.Authentication;
using AskLucy.Application.Behaviors;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.Options;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(config => config.RegisterServicesFromAssembly(assembly));
        services.AddValidatorsFromAssembly(assembly);

        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(MediatR.IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        services.AddScoped<TokenIssuer>();
        services.AddScoped<DefaultProviderResolver>();

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

        return services;
    }
}
