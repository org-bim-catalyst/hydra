using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Ai;
using AskLucy.Infrastructure.Auth;
using AskLucy.Infrastructure.Consent;
using AskLucy.Infrastructure.Email;
using AskLucy.Infrastructure.Files;
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

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName));

        services.AddOptions<CookiePolicyOptions>()
            .Bind(configuration.GetSection(CookiePolicyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDataProtection();

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

        // spec 012-elevenlabs-voice-engine: BaseAddress must be set here, not per-call — both
        // ElevenLabsTextToSpeechProvider and ElevenLabsSpeechToTextSessionProvider issue
        // relative-URI requests against this named client.
        services.AddHttpClient("ElevenLabs", (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ElevenLabsOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISignedUrlService, SignedUrlService>();
        services.AddSingleton<ICookiePolicyProvider, CookiePolicyProvider>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
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
        // Singleton: caches the loaded WhisperFactory (and the one-time model download)
        // across requests instead of reloading it every call. Registered as its concrete
        // type too (mapped to the same instance) so WhisperWarmupHostedService can trigger
        // that load at startup instead of on a user's first request.
        services.AddSingleton<WhisperLocalTranscriptionProvider>();
        services.AddSingleton<ITranscriptionProvider>(sp => sp.GetRequiredService<WhisperLocalTranscriptionProvider>());
        services.AddHostedService<WhisperWarmupHostedService>();
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
