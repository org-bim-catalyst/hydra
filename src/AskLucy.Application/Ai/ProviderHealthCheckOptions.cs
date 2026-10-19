namespace AskLucy.Application.Ai;

/// <summary>
/// Bound from configuration/environment (constitution §8/§22). Governs how often
/// <c>ProviderHealthCheckHostedService</c> checks each enabled provider (FR-027, research.md Decision 7) and the fallback
/// retention window used when the stored singleton setting is missing (specs/082).
/// </summary>
public sealed class ProviderHealthCheckOptions
{
    public const string SectionName = "ProviderHealthCheck";

    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Health-check rows older than this many days are deleted by the daily retention job when no stored setting exists.</summary>
    public int RetentionDays { get; init; } = 30;
}
