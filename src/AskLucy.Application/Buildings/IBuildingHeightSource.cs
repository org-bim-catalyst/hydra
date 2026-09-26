using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Buildings;

/// <summary>
/// Measured building heights around a point, from a source that knows heights but whose footprints
/// are not the ones the viewer draws. The footprints still come from
/// <see cref="IBuildingFootprintProvider"/>; the heights are read off the roofs under each
/// footprint. Kept apart from the footprint provider because the two answer different questions:
/// the best outline for the basemap (rendered from Google's own map) and the best height (measured
/// from imagery) come from different vendors.
/// </summary>
public interface IBuildingHeightSource
{
    /// <summary>
    /// The measured roof heights covering a square of side 2 × <paramref name="radiusMetres"/>
    /// around <paramref name="center"/>; <see cref="BuildingHeightMap.Empty"/> when the source is
    /// switched off. Throws <see cref="BuildingProviderUnavailableException"/> when the source can't
    /// be reached.
    /// </summary>
    Task<BuildingHeightMap> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default);
}
