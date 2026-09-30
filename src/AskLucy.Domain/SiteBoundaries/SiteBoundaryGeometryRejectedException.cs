namespace AskLucy.Domain.SiteBoundaries;

/// <summary>
/// specs/079 — a hand-edited ring the server refuses (contracts/site-boundary-edit-api.md): the
/// request is well-formed, the shape is not acceptable. Mapped to 422 with <see cref="RingIndex"/>
/// and <see cref="Reason"/> so the editor can point at the ring.
/// </summary>
public sealed class SiteBoundaryGeometryRejectedException(int ringIndex, string reason, string message) : Exception(message)
{
    public const string SelfCrossing = "selfCrossing";

    public const string Degenerate = "degenerate";

    public const string DuplicateCorner = "duplicateCorner";

    public const string DriftedAway = "driftedAway";

    public const string TooLarge = "tooLarge";

    public int RingIndex { get; } = ringIndex;

    /// <summary>One of the constants on this type.</summary>
    public string Reason { get; } = reason;
}
