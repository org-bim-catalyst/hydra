using System.Globalization;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Spatial.Query;

/// <summary>specs/095 — spatial relationship operators.</summary>
public enum SpatialRelationship { Within, Outside, Intersecting, Containing, Near }

/// <summary>specs/095 — how the reference feature is supplied.</summary>
public enum SpatialReferenceKind { NamedFeature, ActiveLayer, StudyArea, ExplicitGeometry }

/// <summary>
/// specs/095 — one spatial relationship condition applied to the target dataset.
/// </summary>
public sealed record SpatialPredicate(
    SpatialRelationship Relationship,
    SpatialReferenceKind ReferenceKind,
    string? ReferenceName = null,
    string? ReferenceLayerId = null,
    IReadOnlyList<GeoPoint>? ReferenceGeometry = null,
    double? DistanceMeters = null,
    string? DistanceUnit = null)
{
    /// <summary>Convert user-supplied distance + unit to metres.</summary>
    /// <param name="distanceMeters">Distance value, already in metres if unit is null or "m".</param>
    /// <param name="distanceUnit">User-supplied unit: m, km, ft, mi. Null/empty = metres.</param>
    /// <returns>Distance in metres.</returns>
    public static double ToMetres(double distanceMeters, string? distanceUnit)
        => distanceUnit?.ToLowerInvariant() switch
        {
            "km" => distanceMeters * 1000,
            "ft" => distanceMeters * 0.3048,
            "mi" => distanceMeters * 1609.344,
            _ => distanceMeters,
        };
}
