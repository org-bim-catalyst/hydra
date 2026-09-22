namespace AskLucy.Application.Ai;

/// <summary>
/// Derives a vendor-style fingerprint (e.g. OpenAI's/Anthropic's own key pages) from a plaintext
/// API key — specs/066 research.md Decision 2. Pure and stateless: the only place in the codebase
/// permitted to see a provider's plaintext key without immediately encrypting it is the caller of
/// this method (<c>SetAiProviderCredentialCommandHandler</c>), which also holds it just long enough
/// to call <c>IAiCredentialProtector.Protect</c>.
/// </summary>
public static class CredentialHintFormatter
{
    private const int MinLengthForHint = 8;

    /// <summary>
    /// Returns <c>first4...last4</c> when <paramref name="plaintextApiKey"/> is at least 8
    /// characters (the shortest length at which the two halves cannot overlap), otherwise a fully
    /// masked <c>****</c> placeholder — never a partial or full reveal of a short key (FR-008).
    /// </summary>
    public static string Format(string plaintextApiKey)
    {
        return plaintextApiKey.Length >= MinLengthForHint
            ? $"{plaintextApiKey[..4]}...{plaintextApiKey[^4..]}"
            : "****";
    }
}
