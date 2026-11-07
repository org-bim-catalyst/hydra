using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Spatial.Query;

/// <summary>
/// specs/095 — writes one <c>SpatialQueryExecuted</c> structured log entry per query execution
/// (FR-015 / SC-009 / clarification Q5). Every field from <c>SpatialQueryAuditEntry</c> in the
/// data model is captured, including the full serialized predicate groups and all returned
/// feature IDs, so the audit trail is self-contained without reading provider logs.
/// </summary>
public sealed class SpatialQueryAuditLogger(ILogger<SpatialQueryAuditLogger> logger)
{
    private readonly ILogger<SpatialQueryAuditLogger> _logger = logger;

    /// <summary>
    /// Records the full outcome of one spatial query execution. Captures all predicates,
    /// result count, returned feature IDs and any failure reason (clarification Q5).
    /// </summary>
    /// <param name="query">The resolved query definition.</param>
    /// <param name="status">
    /// <c>"Succeeded"</c>, <c>"Empty"</c>, <c>"Failed"</c>, or <c>"TimedOut"</c>.
    /// </param>
    /// <param name="duration">Wall-clock duration from first call to final batch.</param>
    /// <param name="featureCount">Total features returned across all batches.</param>
    /// <param name="featureIds">
    /// Stable feature IDs for every returned feature; null when query failed.
    /// </param>
    /// <param name="failureReason">Human-readable failure description; null on success.</param>
    /// <param name="userId">Test user ID for audit trail correlation.</param>
    public void Log(
        SpatialQueryDefinition query,
        string status,
        TimeSpan duration,
        int featureCount,
        IReadOnlyList<string>? featureIds = null,
        string? failureReason = null,
        Guid? userId = null)
    {
        var attributeGroupSummary = SummariseAttributeGroup(query.AttributeGroup);
        var spatialGroupSummary = SummariseSpatialGroup(query.SpatialGroup);

        // Cap feature IDs at 500 entries to avoid log-line explosion (clarification Q5).
        var featureIdsSummary = featureIds is { Count: > 0 }
            ? string.Join(",", featureIds.Take(500))
            : null;

        // Use structured logging with named properties for log aggregation tools.
        _logger.LogInformation(
            new EventId(9500, "SpatialQueryExecuted"),
            "SpatialQueryExecuted QueryId={QueryId} DatasetId={DatasetId} " +
            "AttributeGroup={AttributeGroup} SpatialGroup={SpatialGroup} " +
            "Status={Status} DurationMs={DurationMs} FeatureCount={FeatureCount} " +
            "FeatureIds={FeatureIds} FailureReason={FailureReason} UserId={UserId}",
            query.QueryId,
            query.Dataset.Id,
            attributeGroupSummary,
            spatialGroupSummary,
            status,
            (long)duration.TotalMilliseconds,
            featureCount,
            featureIdsSummary,
            failureReason,
            userId?.ToString() ?? "null");
    }

    // ── Predicate Summarisation ───────────────────────────────────────────────────────────────

    private static string SummariseAttributeGroup(PredicateGroup<AttributePredicate>? group)
    {
        if (group is null || group.Predicates.Count == 0)
        {
            return "(none)";
        }

        var parts = group.Predicates.Select(p =>
            $"{p.Field} {p.Operator} {FormatValue(p.Value)}" +
            (p.ValueTo is not null ? $"..{FormatValue(p.ValueTo)}" : string.Empty) +
            (p.Unit is not null ? $" {p.Unit}" : string.Empty));

        var separator = group.Combinator == PredicateCombinator.Or ? " OR " : " AND ";
        return string.Join(separator, parts);
    }

    private static string SummariseSpatialGroup(PredicateGroup<SpatialPredicate>? group)
    {
        if (group is null || group.Predicates.Count == 0)
        {
            return "(none)";
        }

        var parts = group.Predicates.Select(p =>
        {
            var refDesc = p.ReferenceKind switch
            {
                SpatialReferenceKind.NamedFeature => $"named:{p.ReferenceName}",
                SpatialReferenceKind.ActiveLayer => $"layer:{p.ReferenceLayerId}",
                SpatialReferenceKind.StudyArea => "study-area",
                SpatialReferenceKind.ExplicitGeometry => $"geometry[{p.ReferenceGeometry?.Count ?? 0}pts]",
                _ => "unknown",
            };

            return p.DistanceMeters.HasValue
                ? $"{p.Relationship}({refDesc}, {p.DistanceMeters.Value:F0}m)"
                : $"{p.Relationship}({refDesc})";
        });

        var separator = group.Combinator == PredicateCombinator.Or ? " OR " : " AND ";
        return string.Join(separator, parts);
    }

    private static string FormatValue(object? value) =>
        value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
        };
}
