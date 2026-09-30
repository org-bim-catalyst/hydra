using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>What to do with a shape drawn over the outline.</summary>
public enum CombineOperation
{
    /// <summary>Add the shape's ground to the outline (union).</summary>
    Add,

    /// <summary>Take the shape's ground out of the outline (subtraction).</summary>
    Cut,
}

/// <summary>Why <see cref="ISiteRingGeometry.Combine"/> produced no rings.</summary>
public enum CombineFailure
{
    None,
    HoleNotSupported,
    NothingLeft,
}

/// <summary>The outline's rings after a shape was added or cut - empty, with the reason, when the result is unusable.</summary>
public sealed record CombineResult(CombineFailure Failure, IReadOnlyList<IReadOnlyList<GeoPoint>> Rings);

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

    /// <summary>
    /// Adds <paramref name="shape"/> to the outline, or cuts it out. Overlapping rings merge into one, a cut
    /// that divides the outline gives two, and a shape that does not touch it is added as a ring of its own.
    /// The first ring of the result is the one holding the first ring of the input. Open rings throughout.
    /// </summary>
    CombineResult Combine(IReadOnlyList<IReadOnlyList<GeoPoint>> rings, IReadOnlyList<GeoPoint> shape, CombineOperation operation);

    /// <summary>Adds a connected footprint to the edited ring it touches, changing only the ground near the seam.</summary>
    IReadOnlyList<GeoPoint> Join(IReadOnlyList<GeoPoint> editedRing, IReadOnlyList<GeoPoint> footprint);

    /// <summary>Cuts a footprint back out of the edited ring it was joined to, changing only the ground near the seam.</summary>
    IReadOnlyList<GeoPoint> Cut(IReadOnlyList<GeoPoint> editedRing, IReadOnlyList<GeoPoint> footprint);
}
