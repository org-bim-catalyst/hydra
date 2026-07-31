namespace AskLucy.Infrastructure.Ai;

/// <summary>Bound from configuration/environment (constitution §8/§22). Governs how often <see cref="ProviderHealthCheckHostedService"/> checks each enabled provider (FR-027, research.md Decision 7).</summary>
public sealed class ProviderHealthCheckOptions
{
    public const string SectionName = "ProviderHealthCheck";

    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(2);
}
