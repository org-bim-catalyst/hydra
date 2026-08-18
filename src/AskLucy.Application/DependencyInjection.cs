using System.Reflection;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Application.Authentication;
using AskLucy.Application.Behaviors;
using AskLucy.Application.Documents.Commands;
using AskLucy.Application.Documents.Processing;
using AskLucy.Application.Documents.Processing.Stages;
using AskLucy.Application.KnowledgeBases;
using AskLucy.Application.Memory;
using AskLucy.Application.Options;
using AskLucy.Application.Retrieval;
using AskLucy.Application.Retrieval.Indexing;
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

        services.AddOptions<DocumentStorageQuotaOptions>()
            .Bind(configuration.GetSection(DocumentStorageQuotaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
