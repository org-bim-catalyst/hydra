using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Overpass;

/// <summary>
/// specs/089-site-geometry-specialist contracts/overpass-feature-query.md — one OpenStreetMap tag
/// filter for <see cref="IOverpassFeatureQueryProvider"/>. <see cref="Values"/> null means "any
/// value of this key" and matches ways only: an any-value relation query pulls in district-scale
/// areas nobody names as a site (SPEC-042 research #2). Explicit values also match
/// <c>relation["type"="multipolygon"]</c> when <see cref="IncludeRelations"/> is set.
/// </summary>
public sealed record OverpassTagFilter(string Key, IReadOnlyList<string>? Values, bool IncludeRelations = false);

/// <summary>One bounded read of OpenStreetMap features around a point.</summary>
public sealed record OverpassFeatureQuery(
    GeoPoint Center,
    int RadiusMeters,
    IReadOnlyList<OverpassTagFilter> Filters);

/// <summary>
/// One element returned by the query. A <c>way</c>'s geometry is exactly as mapped — open or
/// closed; a <c>relation</c>'s geometry is its largest assembled outer ring, and a relation that
/// cannot close is omitted rather than force-closed.
/// </summary>
public sealed record OverpassFeature(
    string Id,
    string ElementType,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<GeoPoint> Geometry);
