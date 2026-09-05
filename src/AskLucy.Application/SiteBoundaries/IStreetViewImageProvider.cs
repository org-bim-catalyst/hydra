using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// Fetches ground-level Street View imagery, looking from points around a site's perimeter back
/// toward it, for <see cref="IBoundaryVisionAnalyzer"/> to cross-check what satellite imagery
/// alone leaves ambiguous — whether a wall or fence really runs along a given edge, and which
/// side of it something sits on.
/// </summary>
/// <remarks>
/// Optional-enhancement contract, matching <see cref="ISatelliteImageProvider"/>: never throws.
/// Missing coverage near a viewpoint (a private road, a stretch Street View's car never drove) is
/// routine, not an error — that viewpoint is simply absent from the result. An empty list means
/// no ground-level cross-check was available this run; <see cref="IBoundaryVisionAnalyzer"/> still
/// has the satellite image and falls back to that alone.
/// </remarks>
public interface IStreetViewImageProvider
{
    Task<IReadOnlyList<StreetViewImage>> FetchAsync(
        IReadOnlyList<GeoPoint> viewpoints, GeoPoint lookAt, CancellationToken cancellationToken = default);
}
