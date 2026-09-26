using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/077 — a named structure around a resolved site: a named building, or a station mapped
/// onto the footprint of its building.
/// </summary>
/// <param name="Id">The source's own id, e.g. <c>osm_way_259738494</c>.</param>
/// <param name="Name">The display name: English where the source has one.</param>
/// <param name="Names">Every name the source carries (local, English, alternatives) — all of them are matched against the site's.</param>
/// <param name="Ring">The footprint, a closed ring.</param>
public sealed record RelatedSiteBuilding(
    string Id,
    string Name,
    IReadOnlyList<string> Names,
    SiteBoundaryMemberKind Kind,
    IReadOnlyList<GeoPoint> Ring);

/// <summary>
/// specs/077 — every named building and station within <c>radiusMeters</c> of a point. Returns
/// them all, unfiltered: which ones belong to the site is <see cref="SiteBoundaryMembershipService"/>'s
/// decision, not the data source's. Throws <see cref="BoundaryProviderUnavailableException"/> when
/// the source cannot be reached.
/// </summary>
public interface IRelatedSiteBuildingProvider
{
    Task<IReadOnlyList<RelatedSiteBuilding>> FindNamedBuildingsAsync(GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default);
}

/// <summary>
/// specs/077 — joins footprints into outlines. Footprints closer than <c>bridgeGapMeters</c>
/// become one outline: BurJuman's hotel stands 0.5 m off the mall in OSM, which is a mapping gap,
/// not a street. Returns each resulting outline as a closed ring, largest first.
/// </summary>
public interface ISiteFootprintUnion
{
    IReadOnlyList<IReadOnlyList<GeoPoint>> Union(IReadOnlyList<IReadOnlyList<GeoPoint>> rings, double bridgeGapMeters);
}
