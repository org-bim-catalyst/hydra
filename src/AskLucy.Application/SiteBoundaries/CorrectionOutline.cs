using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Domain.Chats;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>specs/079 - the user's hand-edited outline in the one shape a boundary travels in between a capability and the viewer.</summary>
public static class CorrectionOutline
{
    /// <summary>
    /// The outline in force for a chat: the outline Lucy found, with the user's hand edit applied.
    /// Keeps the site name, core polygon and centroid — the edit replaces the rings, area, members
    /// and revision, so resetting restores the found outline untouched.
    /// <para>
    /// Lives here rather than on <see cref="ActiveSiteBoundary"/> so the chat aggregate's own value
    /// object does not have to know another bounded context's aggregate root. The projection is
    /// pure; the caller decides when a correction applies.
    /// </para>
    /// </summary>
    public static ActiveSiteBoundary ApplyCorrection(this ActiveSiteBoundary found, SiteBoundaryCorrection correction)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentNullException.ThrowIfNull(correction);

        return found with
        {
            Polygon = correction.EditedRings[0],
            AreaSquareMeters = correction.AreaSquareMeters,
            ConfidenceLevel = BoundaryConfidenceLevel.High,
            Source = SiteBoundarySource.UserCorrected,
            SourceDetail = "Hand-edited by the user",
            AdditionalPolygons = correction.EditedRings.Skip(1).ToList(),
            Voids = correction.EditedVoids,
            Members = correction.Members,
            Revision = correction.Revision,
            CorrectionId = correction.Id,
        };
    }

    public static ConfirmedSiteBoundaryData ToConfirmed(SiteBoundaryCorrection correction) =>
        new(
            correction.SiteName,
            correction.FoundCentroidLatitude,
            correction.FoundCentroidLongitude,
            correction.EditedRings[0],
            correction.AreaSquareMeters,
            correction.FoundSnapshot.Confidence,
            BoundaryConfidenceLevel.High,
            SiteBoundarySource.UserCorrected,
            "Hand-edited by the user",
            AlternativeCandidateNames: [])
        {
            CorePolygon = correction.FoundSnapshot.CorePolygon,
            AdditionalPolygons = [.. correction.EditedRings.Skip(1)],
            Voids = correction.EditedVoids,
            Members = correction.Members,
            CorrectionId = correction.Id,
        };
}
