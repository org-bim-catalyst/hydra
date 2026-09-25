namespace AskLucy.Application.Options;

/// <summary>
/// Bound from configuration (constitution §4). Retention windows and recording-pipeline sizes for
/// the operational failure trail (specs/074). Every property has a default and nothing is validated
/// on start: a bad value is clamped by <see cref="Normalize"/> rather than crashing the whole host
/// (research D16).
/// </summary>
public sealed class OperationalFailuresOptions
{
    public const string SectionName = "OperationalFailures";

    /// <summary>FR-029 — acknowledged or resolved incidents older than this are removed.</summary>
    public int ResolvedRetentionDays { get; init; } = 90;

    /// <summary>FR-029 — incidents nobody ever triaged are kept longer before removal.</summary>
    public int UnacknowledgedRetentionDays { get; init; } = 180;

    /// <summary>FR-029 — individual occurrences of a kept incident older than this are trimmed.</summary>
    public int OccurrenceRetentionDays { get; init; } = 90;

    /// <summary>FR-022 — occurrences beyond this are counted but not stored.</summary>
    public int MaxStoredOccurrencesPerIncident { get; init; } = 1000;

    /// <summary>Research D2 — bounded channel capacity; a full channel drops (and logs) a report instead of slowing the caller.</summary>
    public int QueueCapacity { get; init; } = 10_000;

    /// <summary>Research D2 — the background writer ingests at most this many reports per DI scope.</summary>
    public int WriterBatchSize { get; init; } = 100;

    /// <summary>Returns a copy with every value clamped to at least 1.</summary>
    public static OperationalFailuresOptions Normalize(OperationalFailuresOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new OperationalFailuresOptions
        {
            ResolvedRetentionDays = Math.Max(1, options.ResolvedRetentionDays),
            UnacknowledgedRetentionDays = Math.Max(1, options.UnacknowledgedRetentionDays),
            OccurrenceRetentionDays = Math.Max(1, options.OccurrenceRetentionDays),
            MaxStoredOccurrencesPerIncident = Math.Max(1, options.MaxStoredOccurrencesPerIncident),
            QueueCapacity = Math.Max(1, options.QueueCapacity),
            WriterBatchSize = Math.Max(1, options.WriterBatchSize),
        };
    }

    /// <summary>True when <see cref="Normalize"/> would change at least one value.</summary>
    public bool NeedsClamping =>
        ResolvedRetentionDays < 1 || UnacknowledgedRetentionDays < 1 || OccurrenceRetentionDays < 1 ||
        MaxStoredOccurrencesPerIncident < 1 || QueueCapacity < 1 || WriterBatchSize < 1;
}
