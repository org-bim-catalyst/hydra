using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai;

/// <summary>
/// contracts/admin.md's provider list shape — never includes the credential value itself
/// (FR-004/FR-031). <see cref="CredentialHint"/> is the one deliberate exception: a vendor-style
/// fingerprint (specs/066), safe to return precisely because it cannot be used to reconstruct the
/// credential.
/// </summary>
public sealed record AdminAiProviderDto(
    Guid Id,
    string ProviderKey,
    string DisplayName,
    bool IsEnabled,
    bool HasCredential,
    string? CredentialHint,
    DateTime? CredentialLastRotatedAtUtc,
    Guid? DefaultModelId,
    ProviderHealthStatus HealthStatus,
    DateTime? HealthStatusCheckedAtUtc,
    AiProviderFailureKind? HealthFailureKind,
    string? HealthFailureReason,
    DateTime? HealthStaleAfterUtc,
    AIProviderKind Kind)
{
    public static AdminAiProviderDto FromEntity(AIProvider provider, DateTime? staleAfterUtc) => new(
        provider.Id,
        provider.ProviderKey,
        provider.DisplayName,
        provider.IsEnabled,
        provider.CredentialCiphertext is not null,
        provider.CredentialHint,
        provider.CredentialLastRotatedAtUtc,
        provider.DefaultModelId,
        provider.HealthStatus,
        provider.HealthStatusCheckedAtUtc,
        provider.HealthFailureKind,
        provider.HealthFailureReason,
        staleAfterUtc,
        provider.Kind);
}
