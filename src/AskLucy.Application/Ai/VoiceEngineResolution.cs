using System.Security.Cryptography;
using AskLucy.Application.Abstractions;
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
