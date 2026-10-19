namespace AskLucy.Application.Ai;

/// <summary>Where the effective retention window came from (specs/082 FR-003).</summary>
public enum ProviderHealthRetentionSource
{
    /// <summary>The stored singleton setting exists and is authoritative.</summary>
    Stored,

    /// <summary>The stored setting is missing; the configured fallback applies.</summary>
    Configuration,
}

/// <summary>
/// specs/082 contracts GET/PUT /api/v1/admin/provider-health/retention. <see cref="RowVersion"/> is the base64 value to
/// send back as <c>If-Match</c>; it is null only when <see cref="Source"/> is <see cref="ProviderHealthRetentionSource.Configuration"/>.
/// </summary>
public sealed record ProviderHealthRetentionDto(
    int RetentionDays,
    double IntervalMinutes,
    ProviderHealthRetentionSource Source,
    string? RowVersion);
