using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Spatial;

/// <summary>Overall outcome of one interpreted request.</summary>
public enum SpatialAnalysisStatus
{
    /// <summary>Every operation produced a result and a source reported complete coverage.</summary>
    Complete,

    /// <summary>At least one operation produced a usable result, but something was missing or failed.</summary>
    Partial,

    /// <summary>The analysis ran successfully and matched nothing. Never a provider failure.</summary>
    Empty,

    /// <summary>No operation produced a result. No layers are delivered.</summary>
    Failed,
}

/// <summary>Outcome of one specialist operation.</summary>
public enum SpatialOperationStatus
{
    Succeeded,
    Empty,
    Failed,
}

/// <summary>What a data source contributed, and whether its coverage was complete.</summary>
public sealed record DataSourceCoverage(
    string SourceId,
    string DisplayName,
    string CoverageStatus,
    string? Explanation = null)
{
    public static DataSourceCoverage Complete(string sourceId, string displayName) =>
        new(sourceId, displayName, "Complete");

    public static DataSourceCoverage Partial(string sourceId, string displayName, string? explanation = null) =>
        new(sourceId, displayName, "Partial", explanation);

    public static DataSourceCoverage Unavailable(string sourceId, string displayName, string? explanation = null) =>
        new(sourceId, displayName, "Unavailable", explanation);
}

/// <summary>
/// Per-operation provenance and status. Always recorded, including failures, so the merged result
/// can explain exactly what happened without reading logs (FR-012/FR-014/FR-025).
/// </summary>
public sealed record SpatialOperationOutcome(
    string OperationId,
    string OperationKind,
    SpatialOperationStatus Status,
    string? SpecialistName = null,
    string? FailureReason = null,
    IReadOnlyList<DataSourceCoverage>? Coverage = null,
    IReadOnlyList<string>? Warnings = null);

/// <summary>
/// The coordinator's normalized output (FR-011). Everything a narration or the viewer needs is
/// here; nothing here has been rendered yet.
/// </summary>
public sealed record SpatialAnalysisResult(
    string AnalysisId,
    string BundleId,
    SpatialAnalysisStatus Status,
    IReadOnlyList<SpatialLayer> Layers,
    int ResultCount,
    IReadOnlyList<DataSourceCoverage> Coverage,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SpatialOperationOutcome> Operations,
    string? ResolvedSummary = null)
{
    /// <summary>True when the request produced at least one layer the viewer can draw.</summary>
    public bool HasLayers => Layers.Count > 0;
}

/// <summary>What kind of shape a layer contributes to the viewer.</summary>
public enum SpatialLayerKind
{
    Markers,
    Buffer,
    Area,
    Band,
}

/// <summary>
/// A layer before the coordinator has validated it. Specialists produce drafts; only the
/// coordinator turns a draft into a <see cref="SpatialLayer"/> (FR-011, constitution §8).
/// </summary>
public sealed record SpatialLayerDraft(
    string LayerId,
    string SourceOperationId,
    string Name,
    string LegendLabel,
    SpatialLayerKind Kind,
    IReadOnlyList<SpatialMarker>? Markers = null,
    IReadOnlyList<GeoPoint>? Ring = null,
    SpatialLayerStyle? Style = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

/// <summary>
/// A validated, renderable layer. Session-scoped: the client owns visibility and removal once it
/// has received it.
/// </summary>
public sealed record SpatialLayer(
    string LayerId,
    string AnalysisId,
    string SourceOperationId,
    string Name,
    string LegendLabel,
    SpatialLayerKind Kind,
    IReadOnlyList<SpatialMarker>? Markers = null,
    IReadOnlyList<GeoPoint>? Ring = null,
    SpatialLayerStyle? Style = null,
    bool Visible = true,
    IReadOnlyDictionary<string, string>? Attributes = null);

/// <summary>One point the viewer draws for a marker layer.</summary>
public sealed record SpatialMarker(string Id, string Name, double Latitude, double Longitude);

/// <summary>Server-assigned styling. Never derived from model text.</summary>
public sealed record SpatialLayerStyle(
    string? Color = null,
    string? MarkerShape = null,
    double? Opacity = null,
    double? StrokeWidth = null);
