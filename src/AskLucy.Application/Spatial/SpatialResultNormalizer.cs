using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Spatial;

/// <summary>What one specialist produced for one operation, including a captured failure.</summary>
public sealed record SpatialSpecialistExecution(
    SpatialOperation Operation,
    string SpecialistName,
    SpatialSpecialistOutcome? Outcome,
    Exception? Error = null);

/// <summary>
/// Turns specialist outcomes into the one normalized result the viewer and narration consume
/// (FR-011/FR-012). Specialist output is untrusted: every layer is validated here before it can
/// become a <see cref="SpatialLayer"/> (constitution §8).
/// </summary>
public sealed class SpatialResultNormalizer
{
    private const int MaxNameLength = 80;
    private const int MaxAttributeLength = 200;

    /// <summary>
    /// Attribute keys a layer may carry to the client. Everything else is dropped — a provider tag
    /// is not a licence to render arbitrary text, and external URLs never reach the viewer.
    /// </summary>
    private static readonly HashSet<string> AllowedAttributeKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "category",
        "address",
        "opening_hours",
        "operator",
        "description",
    };

    public SpatialAnalysisResult Normalize(
        string analysisId,
        string bundleId,
        IReadOnlyList<SpatialSpecialistExecution> executions)
    {
        var layers = new List<SpatialLayer>();
        var operations = new List<SpatialOperationOutcome>();
        var coverage = new List<DataSourceCoverage>();
        var warnings = new List<string>();
        var seenMarkerIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var execution in executions)
        {
            var operation = execution.Operation;
            var outcome = execution.Outcome;

            if (execution.Error is not null || outcome is null || outcome.Status == SpatialOperationStatus.Failed)
            {
                operations.Add(new SpatialOperationOutcome(
                    operation.OperationId,
                    operation.OperationKind,
                    SpatialOperationStatus.Failed,
                    execution.SpecialistName,
                    outcome?.FailureReason ?? execution.Error?.Message));

                var failureWarning =
                    $"The {DescribeKind(operation.OperationKind)} part of the request could not be completed.";
                warnings.Add(failureWarning);
                AddCoverage(coverage, [DataSourceCoverage.Unavailable(
                    operation.OperationKind,
                    DescribeKind(operation.OperationKind),
                    "This part of the request could not be completed.")]);
                continue;
            }

            AddCoverage(coverage, outcome.Coverage);

            if (outcome.Warnings is { Count: > 0 })
            {
                warnings.AddRange(outcome.Warnings);
            }

            if (outcome.Status == SpatialOperationStatus.Empty)
            {
                operations.Add(new SpatialOperationOutcome(
                    operation.OperationId,
                    operation.OperationKind,
                    SpatialOperationStatus.Empty,
                    execution.SpecialistName,
                    Coverage: outcome.Coverage,
                    Warnings: outcome.Warnings));
                continue;
            }

            var accepted = new List<SpatialLayer>();
            foreach (var draft in outcome.Layers)
            {
                if (TryNormalizeLayer(analysisId, draft, warnings, seenMarkerIds) is { } layer)
                {
                    accepted.Add(layer);
                }
            }

            layers.AddRange(accepted);

            operations.Add(new SpatialOperationOutcome(
                operation.OperationId,
                operation.OperationKind,
                accepted.Count > 0 ? SpatialOperationStatus.Succeeded : SpatialOperationStatus.Empty,
                execution.SpecialistName,
                Coverage: outcome.Coverage,
                Warnings: outcome.Warnings));
        }

        var failedCount = operations.Count(o => o.Status == SpatialOperationStatus.Failed);
        var succeededCount = operations.Count(o => o.Status != SpatialOperationStatus.Failed);
        var resultCount = layers.Sum(layer => layer.Markers?.Count ?? (layer.Ring is { Count: > 0 } ? 1 : 0));

        var status = DetermineStatus(failedCount, succeededCount, layers.Count, resultCount, warnings);
        var mergedCoverage = MergeCoverage(coverage);

        return new SpatialAnalysisResult(
            analysisId,
            bundleId,
            status,
            layers,
            resultCount,
            mergedCoverage,
            warnings.Distinct(StringComparer.Ordinal).ToList(),
            operations);
    }

    private static SpatialAnalysisStatus DetermineStatus(
        int failedCount,
        int succeededCount,
        int layerCount,
        int resultCount,
        List<string> warnings)
    {
        if (succeededCount == 0)
        {
            return SpatialAnalysisStatus.Failed;
        }

        if (failedCount > 0 || warnings.Count > 0)
        {
            return SpatialAnalysisStatus.Partial;
        }

        return layerCount == 0 || resultCount == 0
            ? SpatialAnalysisStatus.Empty
            : SpatialAnalysisStatus.Complete;
    }

    private static SpatialLayer? TryNormalizeLayer(
        string analysisId,
        SpatialLayerDraft draft,
        List<string> warnings,
        HashSet<string> seenMarkerIds)
    {
        var name = CleanName(draft.Name, draft.LegendLabel, draft.LayerId);
        var legendLabel = CleanName(draft.LegendLabel, name, draft.LayerId);

        if (string.IsNullOrWhiteSpace(draft.LayerId))
        {
            warnings.Add("A result layer was ignored because it had no identifier.");
            return null;
        }

        IReadOnlyList<SpatialMarker>? markers = null;
        List<GeoPoint>? ring = null;

        switch (draft.Kind)
        {
            case SpatialLayerKind.Markers:
                markers = (draft.Markers ?? [])
                    .Where(IsValidMarker)
                    .Where(marker => seenMarkerIds.Add(marker.Id))
                    .ToList();

                if (markers.Count == 0)
                {
                    return null;
                }

                break;

            case SpatialLayerKind.Buffer:
            case SpatialLayerKind.Area:
            case SpatialLayerKind.Band:
                ring = draft.Ring?
                    .Where(IsValidPoint)
                    .ToList();

                if (ring is not { Count: >= 3 })
                {
                    warnings.Add($"A {DescribeKind(draft.Kind)} layer was ignored because its outline was not valid.");
                    return null;
                }

                // A polygon ring must close; add the first point when the provider omitted it.
                if (ring[0] != ring[^1])
                {
                    ring.Add(ring[0]);
                }

                break;
        }

        return new SpatialLayer(
            draft.LayerId,
            analysisId,
            draft.SourceOperationId,
            name,
            legendLabel,
            draft.Kind,
            markers,
            ring,
            draft.Style ?? DefaultStyle(draft.Kind),
            Visible: true,
            CleanAttributes(draft.Attributes));
    }

    private static bool IsValidMarker(SpatialMarker marker) =>
        !string.IsNullOrWhiteSpace(marker.Id) &&
        double.IsFinite(marker.Latitude) && marker.Latitude is >= -90 and <= 90 &&
        double.IsFinite(marker.Longitude) && marker.Longitude is >= -180 and <= 180;

    private static bool IsValidPoint(GeoPoint point) =>
        double.IsFinite(point.Latitude) && point.Latitude is >= -90 and <= 90 &&
        double.IsFinite(point.Longitude) && point.Longitude is >= -180 and <= 180;

    private static Dictionary<string, string>? CleanAttributes(
        IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null || attributes.Count == 0)
        {
            return null;
        }

        var clean = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in attributes)
        {
            if (!AllowedAttributeKeys.Contains(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (trimmed.Length > MaxAttributeLength ||
                trimmed.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains('<') || trimmed.Contains('>'))
            {
                continue;
            }

            clean[key] = trimmed;
        }

        return clean.Count > 0 ? clean : null;
    }

    private static SpatialLayerStyle DefaultStyle(SpatialLayerKind kind) => kind switch
    {
        SpatialLayerKind.Markers => new SpatialLayerStyle(Color: "#4285F4", MarkerShape: "simple-dot"),
        SpatialLayerKind.Buffer => new SpatialLayerStyle(Color: "#1976D2", Opacity: 0.25, StrokeWidth: 2),
        SpatialLayerKind.Area => new SpatialLayerStyle(Color: "#43A047", Opacity: 0.2, StrokeWidth: 2),
        _ => new SpatialLayerStyle(Color: "#FB8C00", Opacity: 0.2, StrokeWidth: 2),
    };

    private static string CleanName(string? primary, string? fallback, string layerId)
    {
        var value = string.IsNullOrWhiteSpace(primary)
            ? string.IsNullOrWhiteSpace(fallback) ? layerId : fallback
            : primary;

        value = value.Trim();
        return value.Length <= MaxNameLength ? value : value[..MaxNameLength];
    }

    private static string DescribeKind(string operationKind) => operationKind switch
    {
        SpatialOperationKinds.PoiDiscovery => "place search",
        SpatialOperationKinds.ProximityAnalysis => "proximity",
        _ => operationKind,
    };

    private static string DescribeKind(SpatialLayerKind kind) => kind switch
    {
        SpatialLayerKind.Markers => "marker",
        SpatialLayerKind.Buffer => "buffer",
        SpatialLayerKind.Area => "area",
        _ => "band",
    };

    private static void AddCoverage(List<DataSourceCoverage> coverage, IReadOnlyList<DataSourceCoverage>? added)
    {
        if (added is { Count: > 0 })
        {
            coverage.AddRange(added);
        }
    }

    /// <summary>
    /// One entry per source, at the worst reported status. A source that was partial in one
    /// operation is never reported as complete because another operation happened to be fine.
    /// </summary>
    private static List<DataSourceCoverage> MergeCoverage(IReadOnlyList<DataSourceCoverage> coverage)
    {
        static int Rank(string status) => status switch
        {
            "Unavailable" => 2,
            "Partial" => 1,
            _ => 0,
        };

        return coverage
            .GroupBy(c => c.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(c => Rank(c.CoverageStatus)).First())
            .ToList();
    }
}
