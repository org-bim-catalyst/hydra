using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai;

/// <summary>contracts/providers.md's user-facing provider shape — enabled providers only, no credential fields at all.</summary>
public sealed record ProviderSummaryDto(
    Guid Id,
    string ProviderKey,
    string DisplayName,
    ProviderHealthStatus HealthStatus,
    DateTime? HealthStatusCheckedAtUtc)
{
    public static ProviderSummaryDto FromEntity(AIProvider provider) => new(
        provider.Id, provider.ProviderKey, provider.DisplayName, provider.HealthStatus, provider.HealthStatusCheckedAtUtc);
}
