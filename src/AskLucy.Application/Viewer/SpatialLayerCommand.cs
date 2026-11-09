using System.Text.Json;

namespace AskLucy.Application.Viewer;

/// <summary>A single marker the viewer should place for a spatial layer.</summary>
public sealed record SpatialMarkerData(string Id, string Name, double Latitude, double Longitude);

/// <summary>One vertex of a buffer/area/band outline, in WGS-84.</summary>
public sealed record SpatialRingPoint(double Latitude, double Longitude);
public sealed record DensityCellPayload(string Id, IReadOnlyList<SpatialRingPoint> Ring, double Value,
    string Unit, int? ClassificationIndex, string? ClassificationLabel, int SourceCount);
public sealed record DensityClassificationPayload(string Method, IReadOnlyList<double> Breaks, IReadOnlyList<string> Labels);
public sealed record DensitySurfacePayload(string Method, string Unit, double? SearchRadiusMeters,
    double? CellSizeMeters, DensityClassificationPayload Classification, IReadOnlyList<DensityCellPayload> Cells);
public sealed record SpatialFeatureGeometryPayload(string Kind, IReadOnlyList<SpatialRingPoint> Points);
public sealed record OverlayFeatureMetadataPayload(IReadOnlyDictionary<string, bool> Eligibility,
    IReadOnlyDictionary<string, double> CriterionScores, double? Score, int? Rank, IReadOnlyList<string> Provenance);
public sealed record SpatialFeaturePayload(string FeatureId, string Name, SpatialFeatureGeometryPayload Geometry,
    IReadOnlyDictionary<string, object?> Attributes, OverlayFeatureMetadataPayload? Overlay = null);
public sealed record OverlayLayerMetadataPayload(string Role, string Operation, IReadOnlyList<string> Criteria,
    IReadOnlyList<string> Sources, string? ScoreMeaning = null, IReadOnlyList<string>? Coverage = null);

/// <summary>
/// One renderable layer. <see cref="Kind"/> is one of <c>markers</c>, <c>buffer</c>, <c>area</c>,
/// <c>band</c>; a marker layer carries <see cref="Markers"/>, every other kind carries
/// <see cref="Ring"/>.
/// </summary>
public sealed record SpatialLayerPayload(
    string LayerId,
    string SourceOperation,
    string Name,
    string LegendLabel,
    string Kind,
    IReadOnlyList<SpatialMarkerData>? Markers = null,
    IReadOnlyList<SpatialRingPoint>? Ring = null,
    string? Color = null,
    string? MarkerShape = null,
    double? Opacity = null,
    double? StrokeWidth = null,
    bool Visible = true,
    DensitySurfacePayload? Density = null,
    IReadOnlyList<SpatialFeaturePayload>? Features = null,
    OverlayLayerMetadataPayload? Overlay = null,
    IReadOnlyList<SpatialCoveragePayload>? Coverage = null);

/// <summary>What one data source contributed, for the legend's provenance line.</summary>
public sealed record SpatialCoveragePayload(
    string SourceId,
    string DisplayName,
    string CoverageStatus,
    string? Explanation = null);

/// <summary>
/// specs/094 — the normalized spatial result carried on
/// <see cref="Ai.Commands.SendChatMessage.ChatStreamChunk.SpatialLayers"/> exactly as
/// <see cref="ViewerContentCommand"/> is. The frontend owns layer state and rendering; this record
/// says which layers arrived, how each should look, and whether they join or replace the layers
/// already on the map.
/// <para>
/// The same shape is the capability's own output (the narration reads <see cref="Count"/>,
/// <see cref="AreaName"/> and <see cref="Question"/> from it), so there is one DTO rather than two
/// that could drift.
/// </para>
/// </summary>
/// <param name="AnalysisId">Owns the layers; used for clear-analysis.</param>
/// <param name="Status">Complete, Partial or Empty. A failed analysis produces no command.</param>
/// <param name="Mode"><c>replace</c> clears other analyses' layers; <c>keep</c> draws beside them.</param>
/// <param name="Layers">The layers to draw. Empty with <c>replace</c> means clear the old ones.</param>
/// <param name="Warnings">User-readable limitations.</param>
/// <param name="Coverage">Which sources contributed, and how completely.</param>
/// <param name="Count">Total features across layers, for the narration.</param>
/// <param name="AreaName">The resolved area, for the narration.</param>
/// <param name="Question">A clarification to ask instead of drawing anything.</param>
public sealed record SpatialLayerCommand(
    string AnalysisId,
    string Status,
    string Mode,
    IReadOnlyList<SpatialLayerPayload> Layers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SpatialCoveragePayload> Coverage,
    int Count = 0,
    string? AreaName = null,
    string? Question = null)
{
    /// <summary>Serialization shared by the capability and the stream extractor.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
