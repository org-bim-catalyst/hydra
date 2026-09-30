using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>
/// specs/079 (research D5, FR-023) - finds the user's hand-edited outline for the site they are
/// asking about now. Same user, same normalised name AND the same place: the point being resolved is
/// inside the outline Lucy originally found grown by 100 m, or within 250 m of that outline's centre.
/// The place check is what stops a correction of "Central Park" in one city being shown for another
/// city's "Central Park".
/// </summary>
public sealed class SiteBoundaryCorrectionMatcher(ISiteBoundaryCorrectionRepository corrections)
{
    /// <summary>How far outside the found outline still counts as the same site.</summary>
    public const double GrowMeters = 100;

    /// <summary>How far from the found outline's centre still counts as the same site.</summary>
    public const double CentroidMeters = 250;

    public async Task<SiteBoundaryCorrection?> FindAsync(
        string userId, string siteName, GeoPoint point, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(siteName))
        {
            return null;
        }

        var candidates = await corrections.FindCandidatesAsync(
            userId, SiteBoundaryCorrection.NormalizeSiteName(siteName), cancellationToken);

        return candidates
            .Select(c => (Correction: c, Distance: GeometryMath.DistanceMeters(point, new GeoPoint(c.FoundCentroidLatitude, c.FoundCentroidLongitude))))
            .Where(x => IsSamePlace(x.Correction, point, x.Distance))
            .OrderBy(x => x.Distance)
            .Select(x => x.Correction)
            .FirstOrDefault();
    }

    private static bool IsSamePlace(SiteBoundaryCorrection correction, GeoPoint point, double centroidDistance)
    {
        if (centroidDistance <= CentroidMeters)
        {
            return true;
        }

        var found = correction.FoundSnapshot;
        IReadOnlyList<IReadOnlyList<GeoPoint>> foundRings = [found.Polygon, .. found.AdditionalPolygons];
        return foundRings.Any(ring => ring.Count >= 3 &&
            (GeometryMath.Contains(ring, point) || GeometryMath.DistanceToRingMeters(point, ring) <= GrowMeters));
    }
}
