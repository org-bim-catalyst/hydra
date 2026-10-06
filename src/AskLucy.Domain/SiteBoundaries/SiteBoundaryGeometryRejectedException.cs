namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// specs/079 — a hand-edited ring the server refuses (contracts/site-boundary-edit-api.md): the
/// request is well-formed, the shape is not acceptable. Mapped to 422 with <see cref="RingIndex"/>
/// and <see cref="Reason"/> so the editor can point at the ring, and <see cref="VoidIndex"/> when the problem
/// is one of that ring's voids (specs/081).
/// </summary>
public sealed class SiteBoundaryGeometryRejectedException(int ringIndex, string reason, string message, int? voidIndex = null)
    : Exception(message)
{
    public const string SelfCrossing = "selfCrossing";

    public const string Degenerate = "degenerate";

    public const string DuplicateCorner = "duplicateCorner";

    public const string DriftedAway = "driftedAway";

    public const string TooLarge = "tooLarge";

    /// <summary>
    /// Cutting a shape out of the middle of an outline would leave a hole. No longer produced by the outline
    /// editor since specs/081, where such a cut makes a void; kept for callers that still cannot carry voids.
    /// </summary>
    public const string HoleNotSupported = "holeNotSupported";

    /// <summary>The cut took away the whole outline.</summary>
    public const string NothingLeft = "nothingLeft";

    /// <summary>The result would be more separate rings than an outline may have.</summary>
    public const string TooManyRings = "tooManyRings";

    /// <summary>specs/081 — a void touches or crosses the outer edge of its ring.</summary>
    public const string VoidOutsidePart = "voidOutsidePart";

    /// <summary>specs/081 — two voids of one ring touch or overlap.</summary>
    public const string VoidsTouch = "voidsTouch";

    /// <summary>specs/081 — the shape changed nothing (for example a cut drawn inside an existing void).</summary>
    public const string NothingChanged = "nothingChanged";

    /// <summary>specs/081 — a ring has more voids than allowed.</summary>
    public const string TooManyVoids = "tooManyVoids";

    public int RingIndex { get; } = ringIndex;

    /// <summary>Which void of the ring, when the problem is a void (specs/081).</summary>
    public int? VoidIndex { get; } = voidIndex;

    /// <summary>One of the constants on this type.</summary>
    public string Reason { get; } = reason;
}
