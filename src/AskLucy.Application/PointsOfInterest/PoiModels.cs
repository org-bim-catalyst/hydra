using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.PointsOfInterest;

/// <summary>
/// One ANDed constraint inside an Overpass statement, e.g. <c>["amenity"="place_of_worship"]</c>
/// or <c>["religion"="muslim"]</c>. <see cref="Values"/> null means "any value of this key".
/// </summary>
public sealed record PoiTagConstraint(string Key, IReadOnlyList<string>? Values);

/// <summary>A group of constraints that must all hold on the same element (ORed with sibling groups).</summary>
public sealed record PoiTagGroup(IReadOnlyList<PoiTagConstraint> Constraints);

/// <summary>One category of point of interest, with the OpenStreetMap tags that express it.</summary>
/// <param name="Key">Stable, lower-case key carried in the layer payload (e.g. <c>school</c>).</param>
/// <param name="DisplayName">Plural, user-facing name (e.g. <c>Schools</c>).</param>
/// <param name="Color">Marker colour, unique per category so two layers never look alike.</param>
/// <param name="MarkerShape">Marker shape shared with the existing POI overlay styles.</param>
/// <param name="TagGroups">
/// Tag statements to query. Each group is ANDed; the groups themselves are ORed — which is how
/// "mosques" (place_of_worship AND religion=muslim) stays distinguishable from "churches".
/// </param>
public sealed record PoiCategoryDefinition(
    string Key,
    string DisplayName,
    string Color,
    string MarkerShape,
    IReadOnlyList<PoiTagGroup> TagGroups);

/// <summary>A single point of interest returned by a provider.</summary>
/// <param name="Id">Provider element id (e.g. <c>osm_node_123</c>).</param>
/// <param name="Name">Best available name — <c>name:en</c>, then <c>name</c>, then <c>name:ar</c>.</param>
/// <param name="Latitude">WGS-84 latitude.</param>
/// <param name="Longitude">WGS-84 longitude.</param>
/// <param name="Tags">The element's OpenStreetMap tags, already filtered to a small set.</param>
public sealed record PoiFeature(
    string Id,
    string Name,
    double Latitude,
    double Longitude,
    IReadOnlyDictionary<string, string> Tags);

/// <summary>A rectangular search area, in Overpass's (south, west, north, east) order.</summary>
public sealed record PoiBoundingBox(double SouthLatitude, double WestLongitude, double NorthLatitude, double EastLongitude);

/// <summary>One bounded search for points of interest around a centre point or inside a box.</summary>
/// <param name="Center">Used for the radius form, and for the response's own reporting.</param>
/// <param name="RadiusMeters">Used only when <paramref name="Bounds"/> is null.</param>
/// <param name="Category">Which places to look for.</param>
/// <param name="MaxResults">Cap on the elements returned.</param>
/// <param name="Bounds">
/// The named area's own extent, when the geocoder supplied one. For a city this is the city's
/// box; for a country it is the country's — searching a 25 km radius around a country's centre
/// answers a completely different question, and in Oman's case finds almost nothing.
/// </param>
public sealed record PoiQuery(
    GeoPoint Center,
    int RadiusMeters,
    PoiCategoryDefinition Category,
    int MaxResults = 200,
    PoiBoundingBox? Bounds = null);

/// <summary>
/// What the client reports as currently drawn on the map, sent with each chat turn so the
/// server's keep/replace question never contradicts the legend the user is looking at
/// (specs/090 FR-009). Only the key and display name are trusted; the tag definition is always
/// resolved from the server's own catalog.
/// </summary>
public sealed record ActivePoiCategoryInput(string Key, string DisplayName);

/// <summary>
/// Reads points of interest from an open geospatial provider. Defined in Application (never in
/// Infrastructure) so the capability depends on the abstraction it owns (constitution §2.V).
/// </summary>
public interface IPoiQueryProvider
{
    /// <summary>
    /// Returns the matching points of interest, capped at <see cref="PoiQuery.MaxResults"/>.
    /// Throws <c>BoundaryProviderUnavailableException</c> when every provider endpoint fails.
    /// </summary>
    Task<IReadOnlyList<PoiFeature>> QueryAsync(PoiQuery query, CancellationToken cancellationToken = default);
}
