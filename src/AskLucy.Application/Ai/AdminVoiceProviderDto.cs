using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai;

/// <summary>
/// specs/070 contracts/admin-voice.md — one configured voice provider. Never includes the
/// credential value itself; <see cref="CredentialHint"/> is the same deliberate, non-reversible
/// fingerprint exception as <see cref="AdminAiProviderDto.CredentialHint"/> (specs/066).
/// </summary>
public sealed record AdminVoiceProviderDto(
    Guid Id,
    string ProviderKey,
    string DisplayName,
    int Priority,
    bool IsPrimary,
    string? DefaultVoiceId,
    bool RequiresCredential,
    bool HasCredential,
    string? CredentialHint)
{
    public static AdminVoiceProviderDto FromEntity(VoiceProvider provider, bool isPrimary, bool requiresCredential) => new(
        provider.Id,
        provider.ProviderKey,
        provider.DisplayName,
        provider.Priority,
        isPrimary,
        provider.DefaultVoiceId,
        requiresCredential,
        provider.CredentialCiphertext is not null,
        provider.CredentialHint);
}

/// <summary>specs/070 — one text-to-speech engine the platform can speak through, and whether an administrator has added it yet.</summary>
public sealed record VoiceEngineDto(string ProviderKey, string DisplayName, bool RequiresCredential, bool IsAdded);

/// <summary>specs/070 — a synthesized voice sample. Base64 in JSON rather than a raw audio response, so it rides the client's ordinary JSON <c>apiFetch</c> error handling.</summary>
public sealed record VoicePreviewDto(string AudioBase64, string ContentType);
