using AskLucy.Application.Ai.CapabilitySettings;
using AskLucy.Application.Ai.Commands.SendChatMessage;
using AskLucy.Domain.Ai;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.SiteBoundaries;

internal static partial class SiteBoundaryMembershipLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Site boundary for chat {UserChatId}: {MemberCount} buildings carry the name of {SiteName} ({IncludedCount} included)")]
    public static partial void MembersFound(ILogger logger, Guid userChatId, string siteName, int memberCount, int includedCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Site boundary for chat {UserChatId}: the search for buildings of the same development failed, so only the site itself is outlined")]
    public static partial void SearchFailed(ILogger logger, Guid userChatId, Exception exception);
}

/// <summary>
/// specs/077 — finds the buildings that carry a resolved site's name and joins the ones that
/// belong to it into its outline. OpenStreetMap maps BurJuman's mall as one building and its
/// office tower and Arjaan hotel as two more, standing on the same podium; the resolved boundary
/// used to be the mall alone. Which of them count is the user's call — this service proposes
/// (connected buildings in, everything else out) and <see cref="Compose"/> redraws whatever the
/// user then chooses.
/// </summary>
public sealed class SiteBoundaryMembershipService(
    IRelatedSiteBuildingProvider buildingProvider,
    ISiteFootprintUnion footprintUnion,
    ICapabilitySettingsReader settings,
    ILogger<SiteBoundaryMembershipService> logger)
{
    /// <summary>Footprints this close to the site are one structure; OSM leaves BurJuman's hotel 0.5 m off the mall.</summary>
    public const double ConnectedGapMeters = 2.0;

    /// <summary>Beyond this a same-named building is a different site: the tower across from BurJuman stands 26 m off it.</summary>
    public const double MaximumGapMeters = 150.0;

    /// <summary>More than this and the question asked afterwards stops being readable.</summary>
    public const int MaximumMembers = 6;

    private const int MaximumSearchRadiusMeters = 800;

    private static readonly string[] NameTagKeys = ["name", "name:en", "alt_name", "official_name"];

    /// <summary>
    /// Returns <paramref name="boundary"/> with its related buildings listed and the connected
    /// ones joined in; unchanged when the capability setting is off, the site's name is too
    /// generic to match on, or nothing matches. Never throws for a failed search — the site's own
    /// outline is still a correct answer, so the failure is logged and that outline returned.
    /// </summary>
    public async Task<ConfirmedSiteBoundaryData> WithRelatedBuildingsAsync(
        ConfirmedSiteBoundaryData boundary, BoundaryCandidate winner, Guid userChatId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await settings.GetBooleanAsync(AiCapability.BoundaryVision, CapabilitySettingCatalog.IncludeConnectedBuildingsKey, cancellationToken))
            {
                return boundary;
            }

            var cores = SiteNameMatcher.CoresOf(
                [boundary.SiteName, winner.Name, .. NameTagKeys.Select(k => winner.Tags.TryGetValue(k, out var v) ? v : null)]);
            if (cores.Count == 0)
            {
                return boundary;
            }

            var core = boundary.CorePolygon ?? boundary.Polygon;
            var centroid = GeometryMath.Centroid(core);
            var reach = core.Max(p => GeometryMath.DistanceMeters(centroid, p));
            var radius = (int)Math.Min(MaximumSearchRadiusMeters, Math.Ceiling(reach + MaximumGapMeters));

            var found = await buildingProvider.FindNamedBuildingsAsync(centroid, radius, cancellationToken);
            var members = Classify(core, found, cores, winner.Id.Split('+'));
            if (members.Count == 0)
            {
                return boundary;
            }

            SiteBoundaryMembershipLog.MembersFound(logger, userChatId, boundary.SiteName, members.Count, members.Count(m => m.Included));
            return Compose(boundary with { CorePolygon = core }, members);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            SiteBoundaryMembershipLog.SearchFailed(logger, userChatId, ex);
            return boundary;
        }
    }

    /// <summary>
    /// Which of <paramref name="found"/> belong in the question: named like the site, outside its
    /// own outline, and within <see cref="MaximumGapMeters"/> of it. Connected buildings come
    /// first and start included; stations never do — a station serves the site, it is not part
    /// of it.
    /// </summary>
    public static IReadOnlyList<SiteBoundaryMember> Classify(
        IReadOnlyList<GeoPoint> core, IReadOnlyList<RelatedSiteBuilding> found, IReadOnlySet<string> cores, IReadOnlyCollection<string> siteOwnIds) =>
        [.. found
            .Where(b => b.Ring.Count >= 3 && !siteOwnIds.Contains(b.Id))
            .Where(b => !GeometryMath.Contains(core, GeometryMath.Centroid(b.Ring)))
            .Where(b => SiteNameMatcher.Matches(b.Names, cores))
            .Select(b => (Building: b, Gap: GeometryMath.GapMeters(core, b.Ring)))
            .Where(x => x.Gap <= MaximumGapMeters)
            .Select(x =>
            {
                var relation = x.Building.Kind == SiteBoundaryMemberKind.Building && x.Gap <= ConnectedGapMeters
                    ? SiteBoundaryMemberRelation.Connected
                    : SiteBoundaryMemberRelation.Nearby;
                return new SiteBoundaryMember(
                    x.Building.Id, x.Building.Name, x.Building.Kind, relation, x.Gap, x.Building.Ring,
                    Included: relation == SiteBoundaryMemberRelation.Connected);
            })
            .OrderBy(m => m.Relation)
            .ThenBy(m => m.Kind)
            .ThenBy(m => m.GapMeters)
            .Take(MaximumMembers)];

    /// <summary>
    /// Redraws the boundary from its own outline plus the members marked included. The outline
    /// holding the site's centre stays <see cref="ConfirmedSiteBoundaryData.Polygon"/>; an included
    /// member standing apart (across a street) becomes one of
    /// <see cref="ConfirmedSiteBoundaryData.AdditionalPolygons"/>. The area is every outline's.
    /// </summary>
    public ConfirmedSiteBoundaryData Compose(ConfirmedSiteBoundaryData boundary, IReadOnlyList<SiteBoundaryMember> members)
    {
        var core = boundary.CorePolygon ?? boundary.Polygon;
        var included = members.Where(m => m.Included).Select(m => m.Ring).ToList();

        IReadOnlyList<IReadOnlyList<GeoPoint>> outlines = included.Count == 0
            ? [core]
            : footprintUnion.Union([core, .. included], ConnectedGapMeters);
        if (outlines.Count == 0)
        {
            // Nothing traceable came back; the rings as mapped are still correct, only unjoined.
            outlines = [core, .. included];
        }

        var coreCentroid = GeometryMath.Centroid(core);
        var primary = outlines.FirstOrDefault(r => GeometryMath.Contains(r, coreCentroid)) ?? outlines[0];

        return boundary with
        {
            Polygon = primary,
            AreaSquareMeters = outlines.Sum(GeometryMath.AreaSquareMeters),
            CorePolygon = core,
            AdditionalPolygons = [.. outlines.Where(r => !ReferenceEquals(r, primary))],
            Members = members,
        };
    }
}
