using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai;

/// <summary>contracts/admin.md's provider list shape — never includes the credential value itself (FR-004/FR-031).</summary>
public sealed record AdminAiProviderDto(
    Guid Id,
    string ProviderKey,
    string DisplayName,
    bool IsEnabled,
    bool HasCredential,
    DateTime? CredentialLastRotatedAtUtc,
    Guid? DefaultModelId,
    ProviderHealthStatus HealthStatus,
    DateTime? HealthStatusCheckedAtUtc,
    AiProviderFailureKind? HealthFailureKind,
    string? HealthFailureReason,
    DateTime? HealthStaleAfterUtc)
{
    /// <summary>
    /// specs/043 FR-017/FR-019. <paramref name="staleAfterUtc"/> is a computed horizon rather
    /// than a boolean: returning the instant the result goes stale lets an open page re-evaluate
    /// itself against the clock, instead of showing a staleness verdict frozen at render time.
    /// </summary>
    public static AdminAiProviderDto FromEntity(AIProvider provider, DateTime? staleAfterUtc) => new(
        provider.Id,
        provider.ProviderKey,
        provider.DisplayName,
        provider.IsEnabled,
        provider.CredentialCiphertext is not null,
        provider.CredentialLastRotatedAtUtc,
        provider.DefaultModelId,
        provider.HealthStatus,
        provider.HealthStatusCheckedAtUtc,
        provider.HealthFailureKind,
        provider.HealthFailureReason,
        staleAfterUtc);
}
