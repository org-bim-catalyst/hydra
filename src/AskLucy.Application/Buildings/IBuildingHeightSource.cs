using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Buildings;

/// <summary>
/// Measured building heights around a point, from a source that knows heights but whose footprints
/// are not the ones the viewer draws. The footprints still come from
/// <see cref="IBuildingFootprintProvider"/>; each height is joined onto the footprint that contains
/// it. Kept apart from the footprint provider because the two answer different questions: the best
/// outline for the basemap (rendered from Google's own map) and the best height (measured from
/// imagery) come from different vendors.
/// </summary>
public interface IBuildingHeightSource
{
    /// <summary>
    /// Every measured height within <paramref name="radiusMetres"/> of <paramref name="center"/>.
    /// Throws <see cref="BuildingProviderUnavailableException"/> when the source can't be reached.
    /// </summary>
    Task<IReadOnlyList<MeasuredBuildingHeight>> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default);
}

/// <summary>One building's measured height, placed at the centre of that building's own footprint.</summary>
public sealed record MeasuredBuildingHeight(GeoPoint Location, double HeightMetres);
