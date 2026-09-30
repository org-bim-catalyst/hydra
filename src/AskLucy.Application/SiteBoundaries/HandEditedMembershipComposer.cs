using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>What choosing buildings did to a hand-edited outline: its new rings, or why that could not be done.</summary>
public sealed record HandEditedMembershipResult(IReadOnlyList<IReadOnlyList<GeoPoint>> Rings, string? Failure)
{
    public bool Succeeded => Failure is null;
}

/// <summary>
/// specs/079 (research D9, US4) - applies a building choice to an outline the user has edited by hand.
/// The raster union that redraws a found outline would re-trace it and move every hand-placed corner,
/// so this works on the edited rings themselves: a building standing apart is added as a ring of its
/// own; one touching the outline is joined on, changing only the ground near the seam; and taking one
/// out cuts it back off (or drops its ring).
/// </summary>
public sealed class HandEditedMembershipComposer(ISiteRingGeometry geometry)
{
    /// <summary>A footprint this close to a ring is joined to it (the gap a join closes, with room to spare).</summary>
    private const double JoinReachMeters = 3.0;

    public HandEditedMembershipResult Apply(
        IReadOnlyList<IReadOnlyList<GeoPoint>> editedRings,
        IReadOnlyList<SiteBoundaryMember> before,
        IReadOnlyList<SiteBoundaryMember> after)
    {
        var rings = editedRings.Select(r => (IReadOnlyList<GeoPoint>)[.. r]).ToList();
        var previous = before.ToDictionary(m => m.Id, m => m.Included, StringComparer.Ordinal);

        foreach (var member in after)
        {
            var wasIncluded = previous.GetValueOrDefault(member.Id);
            if (member.Included == wasIncluded)
            {
                continue;
            }

            var failure = member.Included ? Add(rings, member) : Remove(rings, member);
            if (failure is not null)
            {
                return new HandEditedMembershipResult([], failure);
            }
        }

        return new HandEditedMembershipResult(rings.Select(Closed).ToList(), null);
    }

    private string? Add(List<IReadOnlyList<GeoPoint>> rings, SiteBoundaryMember member)
    {
        var touching = IndexOfRingNear(rings, member.Ring);
        if (touching >= 0 && member.Relation == SiteBoundaryMemberRelation.Connected)
        {
            rings[touching] = geometry.Join(rings[touching], member.Ring);
            return null;
        }

        var combined = geometry.Combine(rings, member.Ring, CombineOperation.Add);
        if (combined.Failure != CombineFailure.None)
        {
            return $"{member.Name} can't be added to the outline.";
        }

        Replace(rings, combined.Rings);
        return null;
    }

    private string? Remove(List<IReadOnlyList<GeoPoint>> rings, SiteBoundaryMember member)
    {
        var touching = IndexOfRingNear(rings, member.Ring);
        if (touching >= 0 && member.Relation == SiteBoundaryMemberRelation.Connected)
        {
            rings[touching] = geometry.Cut(rings[touching], member.Ring);
            return null;
        }

        var combined = geometry.Combine(rings, member.Ring, CombineOperation.Cut);
        if (combined.Failure == CombineFailure.NothingLeft)
        {
            return $"Taking out {member.Name} would leave nothing of the outline.";
        }

        if (combined.Failure != CombineFailure.None)
        {
            return $"{member.Name} can't be taken out of the outline.";
        }

        Replace(rings, combined.Rings);
        return null;
    }

    private int IndexOfRingNear(List<IReadOnlyList<GeoPoint>> rings, IReadOnlyList<GeoPoint> footprint)
    {
        for (var i = 0; i < rings.Count; i++)
        {
            if (geometry.Intersects([rings[i]], [footprint], JoinReachMeters))
            {
                return i;
            }
        }

        return -1;
    }

    private static void Replace(List<IReadOnlyList<GeoPoint>> rings, IReadOnlyList<IReadOnlyList<GeoPoint>> next)
    {
        rings.Clear();
        rings.AddRange(next);
    }

    private static IReadOnlyList<GeoPoint> Closed(IReadOnlyList<GeoPoint> ring) =>
        ring.Count > 1 && ring[0] == ring[^1] ? ring : [.. ring, ring[0]];
}
