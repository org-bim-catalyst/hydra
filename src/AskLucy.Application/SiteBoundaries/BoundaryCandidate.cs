using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/042-site-boundary-resolution data-model.md — one raw shape returned by
/// <see cref="IBoundaryCandidateProvider"/>, before scoring. Ephemeral — exists only for the
/// duration of one resolution call; never returned directly to a caller (only the winning
/// <see cref="SiteBoundaryResult"/> plus alternative names is).
/// </summary>
public sealed record BoundaryCandidate(
    string Id,
    SiteBoundaryPolygon Polygon,
    SiteBoundarySource Source,
    string Name,
    IReadOnlyDictionary<string, string> Tags,
    double DistanceToCenterMeters,
    double AreaSquareMeters);
