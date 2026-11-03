using AskLucy.Application.Overpass;
using AskLucy.Application.SiteAnalysis.Providers;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using AskLucy.Infrastructure.Overpass;

namespace AskLucy.Infrastructure.SiteAnalysis.Providers;

/// <summary>
/// specs/089-site-geometry-specialist — the only <see cref="ISiteGeometryDataProvider"/>. Reads the
/// recorded parcel/plot outlines and the nearby streets in one bounded Overpass query through the
/// shared <see cref="IOverpassFeatureQueryProvider"/>, so the specialist never sees query syntax and
/// the platform keeps one Overpass transport.
///
/// <para>
/// The radius is derived from the site's own extent rather than a fixed constant — a large site
/// must not be clipped — and capped, so a mis-resolved district-scale polygon cannot turn one
/// analysis into a metropolis-sized download. A point-only site uses a small fixed radius.
/// </para>
/// </summary>
internal sealed class OverpassSiteGeometryDataProvider(
    IOverpassFeatureQueryProvider featureQueryProvider) : ISiteGeometryDataProvider
{
    internal const int MinimumQueryRadiusMeters = 60;
    internal const int MaximumQueryRadiusMeters = 1_500;
    internal const int PointOnlyQueryRadiusMeters = 120;
    internal const int RadiusMarginMeters = 80;

    /// <summary>OSM's parcel/plot boundary tags. Relations are queried too: a parcel is often a multipolygon.</summary>
    internal static readonly IReadOnlyList<string> ParcelBoundaryValues = ["parcel", "cadastral"];

    /// <summary>
    /// Road classes that define street frontage. Footways, cycleways, paths and steps are
    /// deliberately excluded: counting one would inflate a frontage that is not a street.
    /// </summary>
    internal static readonly HashSet<string> RoadClasses =
    [
        "motorway", "trunk", "primary", "secondary", "tertiary",
        "residential", "unclassified", "service", "living_street", "pedestrian", "road",
        "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link",
    ];

    public async Task<DataResolutionOutcome<SiteGeometryFeatureData>> ResolveAsync(
        GeoPoint center,
        IReadOnlyList<GeoPoint>? siteBoundary,
        CancellationToken cancellationToken = default)
    {
        var query = new OverpassFeatureQuery(center, RadiusFor(siteBoundary),
        [
            new OverpassTagFilter("boundary", ParcelBoundaryValues, IncludeRelations: true),
            new OverpassTagFilter("highway", [.. RoadClasses]),
        ]);

        IReadOnlyList<OverpassFeature> features;
        try
        {
            features = await featureQueryProvider.QueryAsync(query, cancellationToken);
        }
        catch (BoundaryProviderUnavailableException ex)
        {
            // An unavailable source is a figure-level outcome, not an exception into the specialist:
            // the tool still delivers the figures it can measure without the network (spec US3).
            return DataResolutionOutcome.Unavailable<SiteGeometryFeatureData>(ex.Message);
        }

        var parcels = features
            .Where(IsParcel)
            .Select(feature => new SiteGeometryParcel(feature.Id, feature.Geometry, feature.Tags))
            .ToList();

        var streets = features
            .Where(IsStreet)
            .Select(feature => new SiteGeometryStreet(feature.Id, NameOf(feature.Tags), feature.Geometry))
            .ToList();

        return DataResolutionOutcome.Confirmed(new SiteGeometryFeatureData(parcels, streets));
    }

    /// <summary>The search radius for one site. Internal for tests.</summary>
    internal static int RadiusFor(IReadOnlyList<GeoPoint>? siteBoundary)
    {
        if (siteBoundary is not { Count: >= 3 })
        {
            return PointOnlyQueryRadiusMeters;
        }

        var (minLat, minLon, maxLat, maxLon) = GeometryMath.BoundingBox(siteBoundary);
        var diagonal = GeometryMath.DistanceMeters(new GeoPoint(minLat, minLon), new GeoPoint(maxLat, maxLon));
        var radius = Math.Ceiling(diagonal / 2) + RadiusMarginMeters;

        return (int)Math.Clamp(radius, MinimumQueryRadiusMeters, MaximumQueryRadiusMeters);
    }

    private static bool IsParcel(OverpassFeature feature) =>
        feature.Tags.TryGetValue("boundary", out var boundary) &&
        ParcelBoundaryValues.Contains(boundary, StringComparer.OrdinalIgnoreCase) &&
        OverpassGeometry.IsClosedRing(feature.Geometry);

    private static bool IsStreet(OverpassFeature feature) =>
        feature.Tags.TryGetValue("highway", out var highway) &&
        RoadClasses.Contains(highway) &&
        feature.Geometry.Count >= 2;

    private static string? NameOf(IReadOnlyDictionary<string, string> tags) =>
        tags.GetValueOrDefault("name") ?? tags.GetValueOrDefault("name:en");
}
