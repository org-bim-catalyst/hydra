using AskLucy.Application.Abstractions;
using AskLucy.Infrastructure.Ai;
using AskLucy.Infrastructure.Auth;
using AskLucy.Infrastructure.Email;
using AskLucy.Infrastructure.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
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

        services.AddOptions<SendGridOptions>()
            .Bind(configuration.GetSection(SendGridOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDataProtection();

        services.AddHttpClient("OpenAI", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISignedUrlService, SignedUrlService>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<IAIProvider, OpenAIProvider>();
        services.AddScoped<IEmailSender, SendGridEmailSender>();

        return services;
    }
}
