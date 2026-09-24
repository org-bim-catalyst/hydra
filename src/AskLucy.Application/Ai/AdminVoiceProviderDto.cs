using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai;

/// <summary>
/// specs/070 contracts/admin-voice.md — one configured voice provider. Never includes the
/// credential value itself; <see cref="CredentialHint"/> is the same deliberate, non-reversible
/// fingerprint exception as <see cref="AdminAiProviderDto.CredentialHint"/> (specs/066).
/// <see cref="ModelStatus"/> and <see cref="ModelStatusReason"/> (specs/072 FR-037) report an
/// on-server engine whose backing custom model can't load; an API-key engine is always Ready.
/// <see cref="VendorEnabled"/> is non-null for an engine whose vendor is listed under Admin → AI
/// providers (ElevenLabs): its key and on/off switch live there, so the credential fields
/// describe that row instead of this one.
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
    string? CredentialHint,
    VoiceProviderModelStatus ModelStatus,
    string? ModelStatusReason,
    bool? VendorEnabled)
{
    /// <param name="modelProblem">Why the provider's hosted model can't load, or null when it can.</param>
    public static AdminVoiceProviderDto FromEntity(VoiceProvider provider, bool isPrimary, bool requiresCredential, string? modelProblem, AIProvider? vendor = null) => new(
        provider.Id,
        provider.ProviderKey,
        provider.DisplayName,
        provider.Priority,
        isPrimary,
        provider.DefaultVoiceId,
        requiresCredential,
        (vendor?.CredentialCiphertext ?? provider.CredentialCiphertext) is not null,
        vendor is null ? provider.CredentialHint : vendor.CredentialHint,
        modelProblem is null ? VoiceProviderModelStatus.Ready : VoiceProviderModelStatus.ModelUnavailable,
        modelProblem,
        vendor?.IsEnabled);
}

/// <summary>specs/072 FR-037 — whether a voice provider's engine can load its model right now.</summary>
public enum VoiceProviderModelStatus
{
    Ready,
    ModelUnavailable,
}

/// <summary>
/// specs/070 — one text-to-speech engine the platform can speak through, and whether an
/// administrator has added it yet. <see cref="IsKeyedAsAiProvider"/>: its key is set under Admin →
/// AI providers, not when adding it here.
/// </summary>
public sealed record VoiceEngineDto(string ProviderKey, string DisplayName, bool RequiresCredential, bool IsAdded, bool IsKeyedAsAiProvider);

/// <summary>specs/070 — a synthesized voice sample. Base64 in JSON rather than a raw audio response, so it rides the client's ordinary JSON <c>apiFetch</c> error handling.</summary>
public sealed record VoicePreviewDto(string AudioBase64, string ContentType);
