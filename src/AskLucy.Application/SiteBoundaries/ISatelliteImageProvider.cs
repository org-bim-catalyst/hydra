using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — fetches an overhead map image crop centered on a point,
/// for <see cref="IBoundaryVisionAnalyzer"/> to inspect. Named for the satellite provider this
/// contract originally had; the shipped implementation (Infrastructure's
/// <c>GoogleSatelliteImageProvider</c>) renders Google's roadmap layer instead — see its remarks
/// for why. Optional-enhancement contract, not a required data source (mirrors the reference
/// notebook's own graceful degradation): a failed or unavailable fetch returns
/// <see langword="null"/> rather than throwing, and callers treat that as "AI vision verification
/// unavailable this run," falling back to the deterministic score alone.
/// </summary>
public interface ISatelliteImageProvider
{
    Task<SatelliteImage?> FetchAsync(GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default);
}
