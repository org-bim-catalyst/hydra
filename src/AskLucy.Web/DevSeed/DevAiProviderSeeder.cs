using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Web.DevSeed;

/// <summary>
/// Dev-only convenience, mirroring <see cref="DevAdminSeeder"/>'s exact shape: ensures at least
/// one <see cref="AIProvider"/>/<see cref="AIModel"/> pair exists so <c>DefaultProviderResolver</c>
/// (which throws <see cref="InvalidOperationException"/> when no enabled provider has an available
/// model — "production deployments are assumed to always have at least one") does not immediately
/// fail every chat turn on a fresh or emptied development database. Runs only in Development (see
/// Program.cs); does nothing once at least one provider already exists, so it never fights an
/// administrator's own configuration.
///
/// <para>
/// Never hardcodes a credential (same rule ADR-0001 established for <see cref="DevAdminSeeder"/>).
/// If <c>SeedAiProvider:OpenAi:ApiKey</c> is configured (via <c>dotnet user-secrets</c>), the seeded
/// provider is enabled with that key, encrypted the same way the admin UI would encrypt one typed
/// in by hand. If it is not configured, the provider and model are still seeded — so the admin
/// "AI Providers" page has something to edit instead of an empty list — but left DISABLED, and the
/// resolver's own exception still fires until either a key is added here or an administrator
/// configures one through the UI. That exception is correct behaviour in that case, not a bug: a
/// disabled provider genuinely cannot serve a chat turn.
/// </para>
/// </summary>
public static class DevAiProviderSeeder
{
    private const string ProviderKey = "openai";
    private const string ProviderDisplayName = "OpenAI";
    private const string ModelKey = "gpt-4o-mini";
    private const string ModelDisplayName = "GPT-4o mini";
    private const string SeedActor = "dev-seed";

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var providers = scope.ServiceProvider.GetRequiredService<IAIProviderRepository>();
        var models = scope.ServiceProvider.GetRequiredService<IAIModelRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if ((await providers.ListAllAsync()).Count > 0)
        {
            // Never touches an existing setup — mirrors DevAdminSeeder's "adminsExist" early-out.
            return;
        }

        var provider = AIProvider.Create(ProviderKey, ProviderDisplayName, SeedActor);

        var apiKey = configuration["SeedAiProvider:OpenAi:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var protector = scope.ServiceProvider.GetRequiredService<IAiCredentialProtector>();
            provider.SetCredential(protector.Protect(apiKey), CredentialHintFormatter.Format(apiKey), SeedActor);
            provider.Enable(SeedActor);
        }

        var model = AIModel.Create(
            provider.Id,
            ModelKey,
            ModelDisplayName,
            contextWindowTokens: 128_000,
            maxOutputTokens: 16_384,
            capabilities: new AIModelCapabilities(
                Streaming: true, Vision: true, FunctionCalling: true, JsonMode: true, Reasoning: false,
                Embeddings: false, ImageInput: true, ImageOutput: false, Audio: false),
            releaseDate: null,
            pricing: null,
            actor: SeedActor);

        provider.SetDefaultModel(model.Id, SeedActor);

        providers.Add(provider);
        models.Add(model);
        await unitOfWork.SaveChangesAsync();

        if (provider.IsEnabled)
        {
            DevSeedLog.AiProviderSeededAndEnabled(logger, ProviderDisplayName, ModelDisplayName);
        }
        else
        {
            DevSeedLog.AiProviderSeededDisabled(logger, ProviderDisplayName);
        }
    }
}

/// <summary>Partial continuation of the <c>DevSeedLog</c> declared in <see cref="DevAdminSeeder"/>'s
/// own file — same namespace, same class, merged by the compiler — rather than a second logging
/// class for what is conceptually the same "dev-only startup seeding" concern.</summary>
internal static partial class DevSeedLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Dev seed AI provider ready and enabled: {ProviderName} / {ModelName}")]
    public static partial void AiProviderSeededAndEnabled(ILogger logger, string providerName, string modelName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dev seed AI provider created but left DISABLED (no SeedAiProvider:OpenAi:ApiKey configured) — set it via `dotnet user-secrets set \"SeedAiProvider:OpenAi:ApiKey\" \"sk-...\"` or configure a key through the admin AI Providers page, or chat turns will keep failing with 'no enabled AI provider has an available model'. Seeded provider: {ProviderName}.")]
    public static partial void AiProviderSeededDisabled(ILogger logger, string providerName);
}
