using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>specs/079 - the user's hand-edited outline in the one shape a boundary travels in between a capability and the viewer.</summary>
public static class CorrectionOutline
{
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
