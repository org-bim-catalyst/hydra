using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteAnalysis.Providers;

/// <summary>One parcel or plot outline the map data records, as returned by <see cref="ISiteGeometryDataProvider"/>.</summary>
public sealed record SiteGeometryParcel(string Id, IReadOnlyList<GeoPoint> Ring, IReadOnlyDictionary<string, string> Tags);

/// <summary>One street near the site. <see cref="Name"/> is null when the map data does not name it.</summary>
public sealed record SiteGeometryStreet(string Id, string? Name, IReadOnlyList<GeoPoint> Geometry);

/// <summary>Everything the geometry source returned for one site: recorded parcel outlines and nearby streets.</summary>
public sealed record SiteGeometryFeatureData(
    IReadOnlyList<SiteGeometryParcel> Parcels,
    IReadOnlyList<SiteGeometryStreet> Streets)
{
    public static SiteGeometryFeatureData Empty { get; } = new([], []);
}

/// <summary>
/// specs/089-site-geometry-specialist — the site-geometry data seam. Defined in Application so the
/// specialist never sees Overpass, HTTP or query syntax (constitution §3); the only implementation
/// reads OpenStreetMap through <see cref="Overpass.IOverpassFeatureQueryProvider"/>.
/// </summary>
public interface ISiteGeometryDataProvider
{
    /// <summary>
    /// Reads the parcel outlines and streets around <paramref name="center"/>. The radius is derived
    /// from <paramref name="siteBoundary"/> when one exists; a point-only site uses a small fixed
    /// search radius. A source failure returns <see cref="DataResolutionOutcomeType.Unavailable"/>
    /// with the reason — it is not an exception into the tool.
    /// </summary>
    Task<DataResolutionOutcome<SiteGeometryFeatureData>> ResolveAsync(
        GeoPoint center,
        IReadOnlyList<GeoPoint>? siteBoundary,
        CancellationToken cancellationToken = default);
}
