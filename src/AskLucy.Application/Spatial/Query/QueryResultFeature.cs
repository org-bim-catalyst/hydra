using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Spatial.Query;

/// <summary>specs/095 — the shape kind of a returned feature geometry.</summary>
public enum FeatureGeometryKind { Point, Polygon, LineString }

/// <summary>specs/095 — geometry for one query result feature.</summary>
/// <param name="Kind">Shape kind; determines how <paramref name="Points"/> is interpreted.</param>
/// <param name="Points">
/// Point: exactly one element. Polygon: closed ring ≥ 4 points. LineString: ≥ 2 points.
/// </param>
public sealed record FeatureGeometry(FeatureGeometryKind Kind, IReadOnlyList<GeoPoint> Points);

/// <summary>
/// specs/095 — one geographic feature returned by an <see cref="ISpatialQueryProvider"/>.
/// </summary>
/// <param name="FeatureId">Stable identifier within the dataset; used in audit log.</param>
/// <param name="Name">Display name. May be empty but not null.</param>
/// <param name="Geometry">The feature's geometry.</param>
/// <param name="Attributes">
/// Attribute values keyed by <see cref="AttributeField.Name"/>. Only declared fields included.
/// </param>
public sealed record QueryResultFeature(
    string FeatureId,
    string Name,
    FeatureGeometry Geometry,
    IReadOnlyDictionary<string, object?> Attributes);
