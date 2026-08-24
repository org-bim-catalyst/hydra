using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Web.DevSeed;

/// <summary>
/// Dev-only convenience: if the baseline-seeded "openai" <c>AIProvider</c> row (migration
/// 20260730174103_AddMultiProviderAiEngine) isn't enabled yet and <c>OpenAI:ApiKey</c> is
/// configured, encrypts and sets that credential, enables the provider, and sets its flagship
/// model as the provider's default — satisfying <c>DefaultProviderResolver</c>'s "at least one
/// enabled provider with an available model" assumption without an admin manually clicking
/// through the UI first. Mirrors <see cref="DevAdminSeeder"/>'s posture exactly: never touches
/// an already-configured provider, never hardcodes a credential in source. Runs only in
/// Development (see Program.cs) — goes straight through the repositories/protector rather than
/// MediatR's <c>SetAiProviderCredentialCommand</c>, since that command requires an
/// HTTP-authenticated <c>ICurrentUserAccessor</c> this startup-time call has none of.
/// </summary>
public static class DevAiProviderSeeder
{
    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var providers = scope.ServiceProvider.GetRequiredService<IAIProviderRepository>();
        var models = scope.ServiceProvider.GetRequiredService<IAIModelRepository>();
        var protector = scope.ServiceProvider.GetRequiredService<IAiCredentialProtector>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var provider = await providers.GetByKeyAsync("openai");
        if (provider is null || provider.IsEnabled)
        {
            return;
        }

        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            DevSeedLog.NoOpenAiApiKeyConfigured(logger);
            return;
        }

        const string actor = "system:dev-seed";
        provider.SetCredential(protector.Protect(apiKey), actor);
        provider.Enable(actor);

        var availableModels = await models.ListAvailableByProviderIdAsync(provider.Id);
        var defaultModel = availableModels.FirstOrDefault(m => m.ModelKey == "gpt-4.1") ?? availableModels.FirstOrDefault();
        if (defaultModel is not null)
        {
            provider.SetDefaultModel(defaultModel.Id, actor);
        }

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        DevSeedLog.OpenAiProviderReady(logger, defaultModel?.DisplayName ?? "(no available model)");
    }
}

internal static partial class DevSeedLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenAI:ApiKey is not configured — skipping dev AI provider seed. Set it via `dotnet user-secrets` to auto-enable OpenAI as the platform default.")]
    public static partial void NoOpenAiApiKeyConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenAI provider enabled with default model {ModelDisplayName}.")]
    public static partial void OpenAiProviderReady(ILogger logger, string modelDisplayName);
}
