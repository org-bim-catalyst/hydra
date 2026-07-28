using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Ai;
using AskLucy.Infrastructure.Auth;
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

        services.AddOptions<LocalFileStorageOptions>()
            .Bind(configuration.GetSection(LocalFileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName));

        services.AddDataProtection();

        services.AddHttpClient("OpenAI", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISignedUrlService, SignedUrlService>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IExternalLoginCodeStore, InMemoryExternalLoginCodeStore>();
        services.AddScoped<IAIProvider, OpenAIProvider>();

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
