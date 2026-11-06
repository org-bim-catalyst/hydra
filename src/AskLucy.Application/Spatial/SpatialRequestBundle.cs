using AskLucy.Application.Locations;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Spatial;

/// <summary>
/// specs/094 — the operation kinds the coordinator can interpret and dispatch. A kind is a stable
/// lower-case string rather than an enum so adding a specialist never edits a shared enum: the
/// declaration below is the one place a new capability is announced to interpretation.
/// </summary>
public static class SpatialOperationKinds
{
    public const string PoiDiscovery = "poi.discovery";
    public const string ProximityAnalysis = "proximity.analysis";

    /// <summary>
    /// Every operation the coordinator is allowed to build. A kind absent here is never produced by
    /// interpretation; a kind present here but unregistered at runtime is reported as not performed
    /// rather than silently dropped (contracts/spatial-request-bundle.md).
    /// </summary>
    public static IReadOnlyList<SupportedSpatialOperation> Supported { get; } =
    [
        new(
            PoiDiscovery,
            "Finds every place of one category in an area and draws them as map markers.",
            "Use when the user asks to see, find or show a CATEGORY of place in an area — \"schools in Muscat\", \"all hospitals in Oman\", \"mosques near here\".",
            "place type (e.g. schools) and area (e.g. Oman)"),
        new(
            ProximityAnalysis,
            "Measures and highlights features within or beyond a distance of a target.",
            "Use when the user asks what is within, beyond, or a given distance from a category, feature or area — \"schools within 1 km of hospitals\", \"areas more than 2 km from a clinic\".",
            "source and target category, distance(s) and unit"),
    ];

    public static bool IsKnown(string? operationKind) =>
        operationKind is not null &&
        Supported.Any(s => string.Equals(s.OperationKind, operationKind, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One operation kind the coordinator can build, with the wording the decider matches on.</summary>
public sealed record SupportedSpatialOperation(
    string OperationKind,
    string Description,
    string WhenToUse,
    string ArgumentHint);

/// <summary>
/// The versioned, structured interpretation of one user request (FR-002/FR-003). Produced by the
/// coordinator and consumed by specialists; never exposed as an HTTP request body.
/// </summary>
/// <param name="BundleVersion">Exactly <see cref="CurrentVersion"/> for this release; anything else is rejected.</param>
/// <param name="BundleId">Identifies one interpreted request in logs and layer ownership.</param>
/// <param name="RequestedAtUtc">When the coordinator interpreted the request.</param>
/// <param name="Operations">One or more operations; ids unique within the bundle.</param>
/// <param name="Preferences">User-expressed layer preferences, if any.</param>
/// <param name="MapContext">The client's current map view, when supplied. Never trusted.</param>
public sealed record SpatialRequestBundle(
    int BundleVersion,
    string BundleId,
    DateTime RequestedAtUtc,
    IReadOnlyList<SpatialOperation> Operations,
    SpatialPreferences? Preferences = null,
    MapViewportContext? MapContext = null)
{
    public const int CurrentVersion = 1;

    public static SpatialRequestBundle Create(
        string bundleId,
        IReadOnlyList<SpatialOperation> operations,
        SpatialPreferences? preferences = null,
        MapViewportContext? mapContext = null,
        DateTime? requestedAtUtc = null) =>
        new(CurrentVersion, bundleId, requestedAtUtc ?? DateTime.UtcNow, operations, preferences, mapContext);
}

/// <summary>One unit of work inside a bundle.</summary>
/// <param name="OperationId">Unique within the bundle; referenced by <paramref name="DependsOn"/>.</param>
/// <param name="OperationKind">One of <see cref="SpatialOperationKinds"/>.</param>
/// <param name="Targets">What the operation is about — a category and/or explicit geometry.</param>
/// <param name="Parameters">Operation-specific inputs. Unused fields are ignored (forward compatibility).</param>
/// <param name="StudyArea">Where the operation runs; required for area-based kinds.</param>
/// <param name="DependsOn">Earlier operation ids that must finish first.</param>
public sealed record SpatialOperation(
    string OperationId,
    string OperationKind,
    IReadOnlyList<FeatureSelector> Targets,
    SpatialOperationParameters? Parameters = null,
    StudyAreaReference? StudyArea = null,
    IReadOnlyList<string>? DependsOn = null);

/// <summary>One thing an operation targets: a category, an explicit geometry, or both.</summary>
/// <param name="Category">Normalized category key or the user's category phrase.</param>
/// <param name="Geometry">An explicit point/line/polygon supplied or resolved earlier.</param>
/// <param name="DataSource">Optional source restriction, constrained to configured sources.</param>
/// <param name="Role">For relational operations: <c>source</c> or <c>target</c>.</param>
public sealed record FeatureSelector(
    string? Category = null,
    IReadOnlyList<GeoPoint>? Geometry = null,
    string? DataSource = null,
    string? Role = null)
{
    public bool HasTarget => !string.IsNullOrWhiteSpace(Category) || Geometry is { Count: > 0 };
}

/// <summary>Operation-specific inputs. All are optional; each specialist validates what it uses.</summary>
public sealed record SpatialOperationParameters(
    IReadOnlyList<SpatialDistanceThreshold>? DistanceThresholds = null,
    string? DistanceUnit = null,
    string? LayerMode = null);

/// <summary>One requested distance band, in the unit the user asked for.</summary>
public sealed record SpatialDistanceThreshold(
    double LowerBound,
    double? UpperBound,
    string? Label = null);

/// <summary>How the user asked for new layers to interact with layers already on the map.</summary>
public enum SpatialLayerMode
{
    /// <summary>Ask the clear-or-keep question before drawing (spec 090's accepted behaviour).</summary>
    Ask,

    /// <summary>Draw beside existing layers.</summary>
    Keep,

    /// <summary>Replace the existing layers.</summary>
    Replace,
}

public sealed record SpatialPreferences(SpatialLayerMode LayerMode = SpatialLayerMode.Ask);

/// <summary>Where an operation's study area comes from.</summary>
public enum StudyAreaKind
{
    NamedArea,
    MapViewport,
    ActiveBoundary,
    ExplicitGeometry,
}

/// <summary>The study area as the user expressed it, before resolution.</summary>
public sealed record StudyAreaReference(
    StudyAreaKind Kind,
    string? NamedArea = null,
    IReadOnlyList<GeoPoint>? Polygon = null);

/// <summary>
/// The client's current map view, captured at send time. Untrusted input: every field is validated
/// before an operation may use it.
/// </summary>
public sealed record MapViewportContext(
    double CenterLatitude,
    double CenterLongitude,
    double Zoom,
    double SouthWestLatitude,
    double SouthWestLongitude,
    double NorthEastLatitude,
    double NorthEastLongitude);

/// <summary>A study area after resolution: a named, bounded area with a centre and radius/extent.</summary>
/// <param name="DisplayName">What the response calls this area.</param>
/// <param name="Center">Centre used for radius searches.</param>
/// <param name="RadiusMeters">Search radius derived from the area's extent or the map view.</param>
/// <param name="Polygon">The area's outline when one is known (active boundary or explicit geometry).</param>
/// <param name="Viewport">The area's bounding box when one is known.</param>
public sealed record ResolvedStudyArea(
    string DisplayName,
    GeoPoint Center,
    double RadiusMeters,
    IReadOnlyList<GeoPoint>? Polygon = null,
    ViewportBounds? Viewport = null);
