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

    /// <summary>
    /// The speech vendors listed under Admin → AI providers (ElevenLabs). A voice engine backed by
    /// one is switched on/off and keyed there — the vendor's single credential, shared with its
    /// health check, model catalogue and live dictation — instead of on its voice-provider row.
    /// </summary>
    public static async Task<IReadOnlyList<AIProvider>> ListSpeechVendorsAsync(
        IAIProviderRepository aiProviders, CancellationToken cancellationToken) =>
        [.. (await aiProviders.ListAllAsync(cancellationToken)).Where(p => p.Kind == AIProviderKind.Speech)];

    /// <summary>The vendor row backing <paramref name="providerKey"/>, or null for an on-server engine.</summary>
    public static AIProvider? FindVendor(IReadOnlyList<AIProvider> speechVendors, string providerKey) =>
        speechVendors.FirstOrDefault(v => string.Equals(v.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase));

    public static async Task<AIProvider?> FindVendorAsync(
        IAIProviderRepository aiProviders, string providerKey, CancellationToken cancellationToken) =>
        FindVendor(await ListSpeechVendorsAsync(aiProviders, cancellationToken), providerKey);

    /// <summary>specs/072 FR-037 — one provider's admin row, including whether its hosted model can load.</summary>
    public static async Task<AdminVoiceProviderDto> ToDtoAsync(
        VoiceProvider provider, bool isPrimary, ITextToSpeechEngine? engine, AIProvider? vendor, CancellationToken cancellationToken)
    {
        var modelProblem = engine is IHostedModelEngine hosted
            ? await hosted.FindModelProblemAsync(cancellationToken)
            : null;
        return AdminVoiceProviderDto.FromEntity(provider, isPrimary, engine?.RequiresCredential ?? false, modelProblem, vendor);
    }

    /// <summary>Every provider's admin row in the given failover order; the first is Lucy's voice.</summary>
    public static async Task<IReadOnlyList<AdminVoiceProviderDto>> ToDtosAsync(
        IEnumerable<ITextToSpeechEngine> engines,
        IReadOnlyList<VoiceProvider> ordered,
        IReadOnlyList<AIProvider> speechVendors,
        CancellationToken cancellationToken)
    {
        var rows = new List<AdminVoiceProviderDto>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var row = ordered[index];
            rows.Add(await ToDtoAsync(
                row, index == 0, FindEngine(engines, row.ProviderKey), FindVendor(speechVendors, row.ProviderKey), cancellationToken));
        }

        return rows;
    }

    /// <summary>
    /// The key to call <paramref name="provider"/>'s engine with: its vendor's when it has one —
    /// refusing while that vendor is switched off — otherwise the row's own. Null when there is
    /// no stored credential (a local engine, or one still using its configuration-file key).
    /// </summary>
    public static string? ResolveApiKey(IAiCredentialProtector protector, VoiceProvider provider, AIProvider? vendor)
    {
        if (vendor is null)
        {
            return DecryptCredential(protector, provider);
        }

        if (!vendor.IsEnabled)
        {
            throw new AiProviderNotConfiguredException($"{vendor.DisplayName} is switched off under Admin → AI providers.");
        }

        return Decrypt(protector, vendor.CredentialCiphertext, vendor.DisplayName);
    }

    /// <summary>Null when the provider has no stored credential (a local engine, or one still using its configuration-file key).</summary>
    public static string? DecryptCredential(IAiCredentialProtector protector, VoiceProvider provider) =>
        Decrypt(protector, provider.CredentialCiphertext, provider.DisplayName);

    private static string? Decrypt(IAiCredentialProtector protector, string? ciphertext, string displayName)
    {
        if (ciphertext is null)
        {
            return null;
        }

        try
        {
            return protector.Unprotect(ciphertext);
        }
        catch (CryptographicException ex)
        {
            throw new AiProviderCredentialUnreadableException(
                $"The stored {displayName} credential could not be decrypted. Replace the API key.", ex);
        }
    }
}
