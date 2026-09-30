using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>Why a ring failed <see cref="ISiteRingGeometry.Validate"/>.</summary>
public enum RingValidationResult
{
    Ok,
    SelfCrossing,
    Degenerate,
    DuplicateCorner,
}

/// <summary>
/// specs/079 (research D3) — the geometry the outline editor needs on the server: validity, union
/// area, and the drift check. Behind a port so Application never references the geometry library
/// (NetTopologySuite lives in Infrastructure only), and so the library can be swapped without
/// touching the handlers. <see cref="Join"/> and <see cref="Cut"/> serve building choices on a
/// hand-edited outline (research D9).
/// </summary>
public interface ISiteRingGeometry
{
    /// <summary>Simple (no self-crossing), area of at least 1 m², no two corners within 0.05 m.</summary>
    RingValidationResult Validate(IReadOnlyList<GeoPoint> ring);

    /// <summary>Area of the union of the rings in square metres; ground covered by more than one ring counts once.</summary>
    double UnionArea(IReadOnlyList<IReadOnlyList<GeoPoint>> rings);

    /// <summary>True when any of <paramref name="rings"/> intersects any of <paramref name="foundRings"/> grown by <paramref name="growMeters"/>.</summary>
    bool Intersects(
        IReadOnlyList<IReadOnlyList<GeoPoint>> rings, IReadOnlyList<IReadOnlyList<GeoPoint>> foundRings, double growMeters);

    /// <summary>Adds a connected footprint to the edited ring it touches, changing only the ground near the seam.</summary>
    IReadOnlyList<GeoPoint> Join(IReadOnlyList<GeoPoint> editedRing, IReadOnlyList<GeoPoint> footprint);

    /// <summary>Cuts a footprint back out of the edited ring it was joined to, changing only the ground near the seam.</summary>
    IReadOnlyList<GeoPoint> Cut(IReadOnlyList<GeoPoint> editedRing, IReadOnlyList<GeoPoint> footprint);
}
