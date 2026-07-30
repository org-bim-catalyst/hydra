using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Ai;
using AskLucy.Infrastructure.Auth;
using AskLucy.Infrastructure.Consent;
using AskLucy.Infrastructure.Email;
using AskLucy.Infrastructure.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        services.AddOptions<WhisperOptions>()
            .Bind(configuration.GetSection(WhisperOptions.SectionName));

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

        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISignedUrlService, SignedUrlService>();
        services.AddSingleton<ICookiePolicyProvider, CookiePolicyProvider>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IExternalLoginCodeStore, InMemoryExternalLoginCodeStore>();
        services.AddScoped<IAIProvider, OpenAIProvider>();
        // Singleton: caches the loaded WhisperFactory (and the one-time model download)
        // across requests instead of reloading it every call. Registered as its concrete
        // type too (mapped to the same instance) so WhisperWarmupHostedService can trigger
        // that load at startup instead of on a user's first request.
        services.AddSingleton<WhisperLocalTranscriptionProvider>();
        services.AddSingleton<ITranscriptionProvider>(sp => sp.GetRequiredService<WhisperLocalTranscriptionProvider>());
        services.AddHostedService<WhisperWarmupHostedService>();

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
