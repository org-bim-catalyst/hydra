using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai;

/// <summary>specs/070 — the engine lookup and credential decryption every admin voice handler that targets one specific provider shares.</summary>
internal static class VoiceEngineResolution
{
    public static ITextToSpeechEngine? FindEngine(IEnumerable<ITextToSpeechEngine> engines, string providerKey) =>
        engines.FirstOrDefault(e => string.Equals(e.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase));

    public static ITextToSpeechEngine GetEngine(IEnumerable<ITextToSpeechEngine> engines, VoiceProvider provider) =>
        FindEngine(engines, provider.ProviderKey)
            ?? throw new KeyNotFoundException($"No text-to-speech engine is installed for '{provider.DisplayName}'.");

    /// <summary>specs/072 FR-037 — one provider's admin row, including whether its hosted model can load.</summary>
    public static async Task<AdminVoiceProviderDto> ToDtoAsync(
        VoiceProvider provider, bool isPrimary, ITextToSpeechEngine? engine, CancellationToken cancellationToken)
    {
        var modelProblem = engine is IHostedModelEngine hosted
            ? await hosted.FindModelProblemAsync(cancellationToken)
            : null;
        return AdminVoiceProviderDto.FromEntity(provider, isPrimary, engine?.RequiresCredential ?? false, modelProblem);
    }

    /// <summary>Every provider's admin row in the given failover order; the first is Lucy's voice.</summary>
    public static async Task<IReadOnlyList<AdminVoiceProviderDto>> ToDtosAsync(
        IEnumerable<ITextToSpeechEngine> engines, IReadOnlyList<VoiceProvider> ordered, CancellationToken cancellationToken)
    {
        var rows = new List<AdminVoiceProviderDto>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            rows.Add(await ToDtoAsync(ordered[index], index == 0, FindEngine(engines, ordered[index].ProviderKey), cancellationToken));
        }

        return rows;
    }

    /// <summary>Null when the provider has no stored credential (a local engine, or one still using its configuration-file key).</summary>
    public static string? DecryptCredential(IAiCredentialProtector protector, VoiceProvider provider)
    {
        if (provider.CredentialCiphertext is null)
        {
            return null;
        }

        try
        {
            return protector.Unprotect(provider.CredentialCiphertext);
        }
        catch (CryptographicException ex)
        {
            throw new AiProviderCredentialUnreadableException(
                $"The stored {provider.DisplayName} credential could not be decrypted. Replace the API key.", ex);
        }
    }
}
