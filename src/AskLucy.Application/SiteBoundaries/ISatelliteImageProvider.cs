using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution — fetches a satellite image crop centered on a point, for
/// <see cref="IBoundaryVisionAnalyzer"/> to inspect. Optional-enhancement contract, not a
/// required data source (mirrors the reference notebook's own graceful degradation): a failed or
/// unavailable fetch returns <see langword="null"/> rather than throwing, and callers treat that
/// as "AI vision verification unavailable this run," falling back to the deterministic score
/// alone.
/// </summary>
public interface ISatelliteImageProvider
{
    Task<SatelliteImage?> FetchAsync(GeoPoint center, int radiusMeters, CancellationToken cancellationToken = default);
}
