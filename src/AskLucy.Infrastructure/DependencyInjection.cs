using AskLucy.Application.Abstractions;
using AskLucy.Application.Locations;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Infrastructure.Agents;
using AskLucy.Infrastructure.Ai;
using AskLucy.Infrastructure.Auth;
using AskLucy.Infrastructure.Boundaries;
using AskLucy.Infrastructure.Consent;
using AskLucy.Infrastructure.Documents;
using AskLucy.Infrastructure.Documents.Extraction;
using AskLucy.Infrastructure.Documents.Ocr;
using AskLucy.Infrastructure.Documents.Preview;
using AskLucy.Infrastructure.Email;
using AskLucy.Infrastructure.Files;
using AskLucy.Infrastructure.Geocoding;
using AskLucy.Infrastructure.KnowledgeBases;
using AskLucy.Infrastructure.Mcp;
using AskLucy.Infrastructure.Memory;
using AskLucy.Infrastructure.Panels;
using AskLucy.Infrastructure.Retrieval;
using AskLucy.Infrastructure.Retrieval.Chunking;
using AskLucy.Infrastructure.Retrieval.Embeddings;
using AskLucy.Infrastructure.Retrieval.VectorStores;
using AskLucy.Infrastructure.Weather;
using AskLucy.Infrastructure.Workflows;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OpenAIOptions>()
            .Bind(configuration.GetSection(OpenAIOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AnthropicOptions>()
            .Bind(configuration.GetSection(AnthropicOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<GoogleGeminiOptions>()
            .Bind(configuration.GetSection(GoogleGeminiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OpenRouterOptions>()
            .Bind(configuration.GetSection(OpenRouterOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ProviderHealthCheckOptions>()
            .Bind(configuration.GetSection(ProviderHealthCheckOptions.SectionName));

        services.AddOptions<WhisperOptions>()
            .Bind(configuration.GetSection(WhisperOptions.SectionName));

        services.AddOptions<ElevenLabsOptions>()
            .Bind(configuration.GetSection(ElevenLabsOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<LocalFileStorageOptions>()
            .Bind(configuration.GetSection(LocalFileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ResumableUploadStorageOptions>()
            .Bind(configuration.GetSection(ResumableUploadStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<TesseractOcrOptions>()
            .Bind(configuration.GetSection(TesseractOcrOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName));

        services.AddOptions<CookiePolicyOptions>()
            .Bind(configuration.GetSection(CookiePolicyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<KnowledgeBasePurgeOptions>()
            .Bind(configuration.GetSection(KnowledgeBasePurgeOptions.SectionName));

        services.AddOptions<OpenAiEmbeddingOptions>()
            .Bind(configuration.GetSection(OpenAiEmbeddingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<OnnxEmbeddingOptions>()
            .Bind(configuration.GetSection(OnnxEmbeddingOptions.SectionName));

        services.AddOptions<PineconeOptions>()
            .Bind(configuration.GetSection(PineconeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // specs/027-immersive-viewer-platform: no ApiKey to validate (research.md Decision 6 —
        // both upstream services are keyless), so no .ValidateDataAnnotations()/.ValidateOnStart()
        // needed beyond binding the (rarely overridden) base URLs.
        services.AddOptions<WeatherOptions>()
            .Bind(configuration.GetSection(WeatherOptions.SectionName));

        // specs/037-location-query-resolution — Nominatim forward geocoding; no ApiKey to
        // validate (same reasoning as WeatherOptions above). ValidateOnStart ensures the
        // SearchBaseUrl is always configured before first request.
        services.AddOptions<GeocodingOptions>()
            .BindConfiguration(GeocodingOptions.SectionName)
            .ValidateOnStart();

        // Google Maps Geocoding API — used when Geocoding:GoogleMapsApiKey is set; falls back
        // to NominatimGeocodingProvider otherwise (local dev / environments without a key).
        services.AddOptions<GoogleMapsGeocodingOptions>()
            .BindConfiguration(GoogleMapsGeocodingOptions.SectionName)
            .ValidateOnStart();

        // specs/042-site-boundary-resolution — OSM Overpass boundary-candidate search; no
        // ApiKey to validate (free, keyless API, same reasoning as GeocodingOptions above).
        services.AddOptions<OverpassOptions>()
            .BindConfiguration(OverpassOptions.SectionName)
            .ValidateOnStart();

        // Document Intelligence Pipeline's durable job engine (specs/015-document-intelligence-
        // pipeline, research.md Decision 2). Connection string resolved lazily from the
        // container's IConfiguration at configuration time, not eagerly from the `configuration`
        // parameter captured here — same reasoning as AddPersistence's DbContext registration.
        services.AddHangfire((sp, config) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>()
                .GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            config.UseSqlServerStorage(connectionString);
        });
        services.AddHangfireServer();
        // Resolved by Hangfire's job activator for both the on-demand processing pipeline jobs
        // and this US6 recurring job — Hangfire resolves concrete job classes directly from the
        // container, not just through their interfaces, so this explicit registration is required.
        services.AddScoped<DocumentStatisticsRecomputeJob>();
        // AI Memory System (specs/018-ai-memory-system) — Foundational. Concrete registrations:
        // Hangfire's RecurringJob.AddOrUpdate<T> resolves recurring jobs by concrete type, same as
        // DocumentStatisticsRecomputeJob above.
        services.AddScoped<MemoryExtractionSweepJob>();
        services.AddScoped<MemoryCleanupJob>();
        // spec 021-mcp-integration User Story 6 — same "Hangfire resolves concrete job classes
        // directly" reasoning as the jobs above.
        services.AddScoped<McpServerHealthCheckJob>();
        services.AddScoped<McpCapabilityRefreshJob>();

        services.AddDataProtection();
        // Concrete IMemoryCache registration for KnowledgeBaseDashboardSummaryCache (Application) — see that DI's comment for why the registration itself lives here, not in Application.
        services.AddMemoryCache();

        services.AddHttpClient("OpenAI", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        services.AddHttpClient("Anthropic", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        services.AddHttpClient("GoogleGemini", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        services.AddHttpClient("OpenRouter", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        // spec 021-mcp-integration (research.md Decision 11): no BaseAddress — this client serves
        // many different, admin-registered MCP server endpoints. HttpClient.Timeout here is only an
        // outer safety net; the precise per-call bound (FR-051, McpRuntimeOptions.MaxCallDurationSeconds)
        // is enforced via a linked CancellationToken at the call site (McpClient/McpClientFactory).
        services.AddHttpClient("Mcp", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        // spec 012-elevenlabs-voice-engine: BaseAddress must be set here, not per-call — both
        // ElevenLabsTextToSpeechProvider and ElevenLabsSpeechToTextSessionProvider issue
        // relative-URI requests against this named client.
        services.AddHttpClient("ElevenLabs", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ElevenLabsOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        // ADR-0007: PineconeVectorStore issues relative-URI requests against this named client.
        services.AddHttpClient("Pinecone", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<PineconeOptions>>().Value;
            client.BaseAddress = new Uri(options.IndexHost);
            client.DefaultRequestHeaders.Add("Api-Key", options.ApiKey);
            client.DefaultRequestHeaders.Add("X-Pinecone-Api-Version", options.ApiVersion);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // specs/027-immersive-viewer-platform: no BaseAddress — WeatherProvider calls two
        // different hosts (Open-Meteo forecast, Nominatim reverse geocoding) with full URIs,
        // same reasoning as the "Mcp" client above.
        services.AddHttpClient("Weather", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // specs/037-location-query-resolution: dedicated client for Google Maps Geocoding API
        // (and Nominatim, if falling back). Kept separate from "Weather" so geocoding and
        // weather timeouts/policies can diverge without coupling.
        // 30s, not 15s: same class of bug as "Overpass" below — observed live (2026-08-27),
        // Google's own Cloud Console Geocoding metrics showed near-zero request volume and ZERO
        // error responses for a period where the app was reliably surfacing "I couldn't look
        // that up right now" (LocationConfirmationTemplates.Unavailable) — i.e. requests weren't
        // reaching Google's servers at all (ruling out the key/quota/billing/API-enablement),
        // consistent with this client's own short timeout aborting slow-but-otherwise-fine calls
        // from this host before Google ever responded.
        services.AddHttpClient("Geocoding", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // specs/042-site-boundary-resolution: dedicated client for the OSM Overpass API — kept
        // separate from "Geocoding" so boundary-search and point-geocoding timeouts/policies can
        // diverge without coupling (same reasoning as "Geocoding" itself vs. "Weather").
        // 30s, not 15s: the query itself declares "[timeout:25]" to the Overpass server (research.md
        // #2/#12) — a 15s client-side timeout could abort a request Overpass was still legitimately
        // working on, turning a slow-but-successful lookup into a false "Unavailable" (observed live:
        // Al Safa Park 2's boundary lookup failed in production despite the identical query
        // succeeding in ~1.4s from a different network — the public instance's latency varies by
        // caller). 30s gives Overpass's own budget room to actually finish before the client gives up.
        services.AddHttpClient("Overpass", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // specs/042-site-boundary-resolution — dedicated client for ESRI World Imagery's free,
        // no-key export endpoint, feeding the Gemini vision cross-check. 30s matches the
        // reference notebook's own hard_timeout(30) around the equivalent fetch.
        services.AddHttpClient("EsriWorldImagery", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISignedUrlService, SignedUrlService>();
        services.AddSingleton<ICookiePolicyProvider, CookiePolicyProvider>();
        services.AddScoped<IWeatherProvider, WeatherProvider>();
        // Use Google Maps when a server-side API key is configured; fall back to Nominatim
        // for local dev and environments without a key.
        if (!string.IsNullOrWhiteSpace(configuration["Geocoding:GoogleMapsApiKey"]))
            services.AddScoped<IGeocodingProvider, GoogleMapsGeocodingProvider>();
        else
            services.AddScoped<IGeocodingProvider, NominatimGeocodingProvider>();
        services.AddScoped<IBoundaryCandidateProvider, OverpassBoundaryCandidateProvider>();
        services.AddScoped<ISatelliteImageProvider, EsriSatelliteImageProvider>();
        services.AddScoped<IBoundaryVisionAnalyzer, GeminiBoundaryVisionAnalyzer>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IDocumentContentValidator, DocumentContentValidator>();
        services.AddSingleton<IDocumentPageCountExtractor, DocumentPageCountExtractor>();
        services.AddSingleton<IDocumentFileValidator, DocumentFileValidator>();
        services.AddSingleton<IResumableUploadStorage, ResumableUploadStorage>();
        services.AddScoped<IOcrEngine, TesseractOcrEngine>();
        services.AddSingleton<IDocumentTextExtractor, OpenXmlTextExtractor>();
        services.AddSingleton<IDocumentTextExtractor, DocnetPdfTextExtractor>();
        services.AddSingleton<IDocumentPreviewGenerator, PdfPreviewGenerator>();
        services.AddSingleton<IDocumentPreviewGenerator, ImageThumbnailGenerator>();
        services.AddScoped<IDocumentLanguageAndClassifier, AiDocumentLanguageAndClassifier>();
        services.AddScoped<IProcessingNotifier, ProcessingNotifier>();

        // Retrieval (specs/016-rag-semantic-search) — Foundational.
        services.AddSingleton<IChunkingStrategy, FixedSizeChunkingStrategy>();
        services.AddSingleton<IChunkingStrategy, RecursiveChunkingStrategy>();
        // Registered as itself too — HeadingChunkingStrategy/TableChunkingStrategy take a
        // concrete ParagraphChunkingStrategy as their fallback constructor dependency, not the
        // IChunkingStrategy interface (which would resolve ambiguously across all 8 strategies).
        services.AddSingleton<ParagraphChunkingStrategy>();
        services.AddSingleton<IChunkingStrategy>(sp => sp.GetRequiredService<ParagraphChunkingStrategy>());
        services.AddSingleton<IChunkingStrategy, SentenceChunkingStrategy>();
        services.AddSingleton<IChunkingStrategy, MarkdownChunkingStrategy>();
        services.AddSingleton<IChunkingStrategy, HeadingChunkingStrategy>();
        services.AddSingleton<IChunkingStrategy, TableChunkingStrategy>();
        services.AddScoped<IChunkingStrategy, SemanticChunkingStrategy>();
        services.AddScoped<IChunkingService, ChunkingService>();
        services.AddScoped<IEmbeddingService, OpenAiEmbeddingProvider>();
        services.AddSingleton<OnnxLocalEmbeddingProvider>();
        services.AddSingleton<IEmbeddingService>(sp => sp.GetRequiredService<OnnxLocalEmbeddingProvider>());
        services.AddScoped<IEmbeddingServiceResolver, EmbeddingServiceResolver>();
        // ADR-0007: Pinecone, the second IVectorStore implementation. SqlServerVectorStore's
        // registration stays in Persistence/DependencyInjection.cs (it needs AskLucyDbContext) —
        // both are unkeyed IVectorStore registrations resolved only through IVectorStoreResolver
        // from here on, never injected directly (constitution §2-VIII: an unkeyed
        // GetRequiredService<IVectorStore>() with two registrations would silently resolve
        // whichever was registered last).
        services.AddScoped<IVectorStore, PineconeVectorStore>();
        services.AddScoped<IVectorStoreResolver, VectorStoreResolver>();
        services.AddScoped<IRetrievalIndexingNotifier, RetrievalIndexingNotifier>();
        services.AddSingleton(TimeProvider.System);
        services.AddHostedService<KnowledgeBasePurgeHostedService>();
        services.AddSingleton<IExternalLoginCodeStore, InMemoryExternalLoginCodeStore>();
        // Unkeyed: legacy single-model call sites (Translate, image generation,
        // AppendMessageCommandHandler attribution) predate multi-provider selection and stay
        // wired to OpenAI directly, per IAIProvider.cs's doc comment.
        services.AddScoped<IAIProvider, OpenAIProvider>();

        // Keyed: multi-provider chat/comparison flows resolve by provider key via
        // IAIProviderResolver (research.md Decision 3) — never by concrete type.
        services.AddKeyedScoped<IAIProvider, OpenAIProvider>("openai");
        services.AddKeyedScoped<IAIProvider, AnthropicProvider>("anthropic");
        services.AddKeyedScoped<IAIProvider, GoogleGeminiProvider>("google-gemini");
        services.AddKeyedScoped<IAIProvider, OpenRouterProvider>("openrouter");
        services.AddScoped<IAIProviderResolver, AiProviderResolver>();
        services.AddSingleton<IAiCredentialProtector, AiCredentialProtector>();
        // AI Memory System (specs/018-ai-memory-system, research.md Decision 12) — dedicated,
        // purpose-scoped protector, not a reuse of IAiCredentialProtector's singleton.
        services.AddSingleton<IMemoryContentProtector, MemoryContentProtector>();
        services.AddScoped<IMemoryNotifier, MemoryNotifier>();
        // AI Agent Framework & Agent Runtime (specs/020-ai-agent-framework) — User Story 4
        // ("Real-Time Execution Visibility"). AgentExecutionHub/AgentExecutionNotifier live here
        // (not Application) for the same reason MemoryHub/MemoryNotifier do — Application must
        // never reference SignalR directly (constitution §3).
        services.AddScoped<IAgentExecutionNotifier, AgentExecutionNotifier>();

        // Workflow & Tool Orchestration Engine (specs/022-workflow-orchestration-engine) — User
        // Story 6 ("Real-Time Monitoring, Pause, Resume, and Cancel"). WorkflowExecutionHub/
        // WorkflowExecutionNotifier live here for the same reason AgentExecutionHub/
        // AgentExecutionNotifier do — Application must never reference SignalR directly (constitution §3).
        services.AddScoped<IWorkflowExecutionNotifier, WorkflowExecutionNotifier>();

        // AI-to-UI Floating Panel Framework (specs/028-ai-floating-panels) — User Story 1.
        // PanelHub/PanelNotifier live here for the same reason AgentExecutionHub/
        // AgentExecutionNotifier do — Application must never reference SignalR directly (constitution §3).
        services.AddScoped<IPanelNotifier, PanelNotifier>();

        // MCP Integration (specs/021-mcp-integration) — Foundational. IMcpClientFactory is a
        // singleton (research.md Decision 2, corrected during implementation — see plan.md): its
        // connection cache spans every execution, not one DI scope, and it resolves the Scoped
        // IMcpServerRepository from a short-lived internal scope only when connecting, never
        // holding it (constitution §3). IMcpEndpointValidator/IMcpCredentialProtector/
        // IMcpRateLimiter have no Scoped dependencies of their own, so they are singleton-safe too.
        services.AddSingleton<IMcpClientFactory, McpClientFactory>();
        services.AddSingleton<IMcpEndpointValidator, McpEndpointValidator>();
        services.AddSingleton<IMcpCredentialProtector, McpCredentialProtector>();
        services.AddSingleton<IMcpRateLimiter, McpRateLimiter>();

        // Singleton: caches the loaded WhisperFactory (and the one-time model download)
        // across requests instead of reloading it every call. Registered as its concrete
        // type too (mapped to the same instance) so WhisperWarmupHostedService can trigger
        // that load at startup instead of on a user's first request.
        services.AddSingleton<WhisperLocalTranscriptionProvider>();
        services.AddSingleton<ITranscriptionProvider>(sp => sp.GetRequiredService<WhisperLocalTranscriptionProvider>());
        services.AddHostedService<WhisperWarmupHostedService>();
        // specs/043 FR-019 - stateless, so a singleton; shares the same options instance the
        // hosted service reads, which is what keeps the window and the interval in step.
        services.AddSingleton<IProviderHealthFreshnessPolicy, ProviderHealthFreshnessPolicy>();
        services.AddHostedService<ProviderHealthCheckHostedService>();

        services.AddScoped<ITextToSpeechProvider, ElevenLabsTextToSpeechProvider>();
        services.AddScoped<ISpeechToTextSessionProvider, ElevenLabsSpeechToTextSessionProvider>();
        services.AddScoped<IVoiceProviderHealthRecorder, VoiceProviderHealthRecorder>();

        // Dev-only: lets a fresh clone complete first registration/login without real SMTP
        // credentials (spec.md convergence note) — Production/Testing/every other environment
        // always uses the real sender.
        if (environment.IsDevelopment())
        {
            services.AddScoped<IEmailSender, ConsoleEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }

        return services;
    }
}
