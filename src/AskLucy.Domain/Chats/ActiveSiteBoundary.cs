using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Domain.Chats;

/// <summary>
/// specs/042-site-boundary-resolution — the currently confirmed site boundary the viewer has
/// highlighted, mirroring <see cref="ActiveSiteLocation"/> exactly (research.md #10/#11).
/// Owned by <see cref="UserChat"/> so it survives the turn boundary; a repeated reference to the
/// same site reuses this value instead of forcing a fresh resolution (FR-009).
/// </summary>
public sealed record ActiveSiteBoundary(
    string SiteName,
    double CentroidLatitude,
    double CentroidLongitude,
    IReadOnlyList<GeoPoint> Polygon,
    double AreaSquareMeters,
    double Confidence,
    BoundaryConfidenceLevel ConfidenceLevel,
    SiteBoundarySource Source,
    string SourceDetail)
{
    /// <summary>
    /// specs/077 — the site's own outline before any <see cref="Members"/> were joined to it. Null
    /// when nothing was ever joined, which is every boundary stored before specs/077: then
    /// <see cref="Polygon"/> is the site's own outline.
    /// </summary>
    public IReadOnlyList<GeoPoint>? CorePolygon { get; init; }

    /// <summary>specs/077 — outlines of included members that stand apart from <see cref="Polygon"/>, e.g. a tower across the street.</summary>
    public IReadOnlyList<IReadOnlyList<GeoPoint>> AdditionalPolygons { get; init; } = [];

    /// <summary>specs/077 — buildings carrying the site's name, and whether the user included each.</summary>
    public IReadOnlyList<SiteBoundaryMember> Members { get; init; } = [];

    /// <summary>
    /// specs/079 — changes whenever the outline in force changes, so a client editing an older shape
    /// is refused (research D4). For an effective boundary it is the correction's revision.
    /// </summary>
    public Guid Revision { get; init; }

    /// <summary>specs/079 — the user's <c>SiteBoundaryCorrection</c> this chat is linked to; null when the chat shows the outline as found.</summary>
    public Guid? CorrectionId { get; init; }

    /// <summary>specs/079 — true when this is a boundary built from a hand edit (<see cref="SiteBoundarySource.UserCorrected"/>).</summary>
    public bool IsHandEdited => Source == SiteBoundarySource.UserCorrected;

    /// <summary>
    /// specs/079 — the outline in force for a chat linked to <paramref name="correction"/>. Replaces
    /// the rings, area, members and revision; keeps the site name, core polygon and centroid. Pure:
    /// the stored boundary is never overwritten, so a reset restores it untouched.
    /// </summary>
    public ActiveSiteBoundary WithCorrection(SiteBoundaryCorrection correction)
    {
        ArgumentNullException.ThrowIfNull(correction);

        return this with
        {
            Polygon = correction.EditedRings[0],
            AreaSquareMeters = correction.AreaSquareMeters,
            ConfidenceLevel = BoundaryConfidenceLevel.High,
            Source = SiteBoundarySource.UserCorrected,
            SourceDetail = "Hand-edited by the user",
            AdditionalPolygons = correction.EditedRings.Skip(1).ToList(),
            Members = correction.Members,
            Revision = correction.Revision,
            CorrectionId = correction.Id,
        };
    }
}
