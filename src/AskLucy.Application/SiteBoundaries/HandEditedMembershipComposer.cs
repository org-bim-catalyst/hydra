using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.SiteBoundaries;

/// <summary>What choosing buildings did to a hand-edited outline: its new rings, or why that could not be done.</summary>
public sealed record HandEditedMembershipResult(IReadOnlyList<IReadOnlyList<GeoPoint>> Rings, string? Failure)
{
    /// <summary>specs/081 - each ring's voids by ring index, closed; the building choice keeps them (it only reduces the area).</summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>> Voids { get; init; } = [];

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
        IReadOnlyList<SiteBoundaryMember> after) =>
        Apply(editedRings, [], before, after);

    /// <summary>
    /// specs/081 - as above, for rings that carry voids (<c>editedVoids[i]</c> belong to <c>editedRings[i]</c>).
    /// A void is kept through the choice: ground a joined building covers is filled in, and a void a cut opens
    /// onto the outside stops being one. Which buildings are members is not affected.
    /// </summary>
    public HandEditedMembershipResult Apply(
        IReadOnlyList<IReadOnlyList<GeoPoint>> editedRings,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>> editedVoids,
        IReadOnlyList<SiteBoundaryMember> before,
        IReadOnlyList<SiteBoundaryMember> after)
    {
        var rings = editedRings.Select(r => (IReadOnlyList<GeoPoint>)[.. r]).ToList();
        var voids = editedRings
            .Select((_, i) => (IReadOnlyList<IReadOnlyList<GeoPoint>>)[.. (i < editedVoids.Count ? editedVoids[i] : [])])
            .ToList();
        var previous = before.ToDictionary(m => m.Id, m => m.Included, StringComparer.Ordinal);

        foreach (var member in after)
        {
            var wasIncluded = previous.GetValueOrDefault(member.Id);
            if (member.Included == wasIncluded)
            {
                continue;
            }

            var failure = member.Included ? Add(rings, voids, member) : Remove(rings, voids, member);
            if (failure is not null)
            {
                return new HandEditedMembershipResult([], failure);
            }
        }

        return new HandEditedMembershipResult(rings.Select(Closed).ToList(), null)
        {
            Voids = [.. voids.Select(ringVoids => (IReadOnlyList<IReadOnlyList<GeoPoint>>)[.. ringVoids.Select(Closed)])],
        };
    }

    private string? Add(
        List<IReadOnlyList<GeoPoint>> rings, List<IReadOnlyList<IReadOnlyList<GeoPoint>>> voids, SiteBoundaryMember member)
    {
        var touching = IndexOfRingNear(rings, member.Ring);
        if (touching >= 0 && member.Relation == SiteBoundaryMemberRelation.Connected)
        {
            // Only the seam changes. A void the building stands over is filled in; the rest stay.
            var ringVoids = voids[touching];
            var joined = geometry.Join(rings[touching], member.Ring);
            if (ringVoids.Count > 0)
            {
                var filled = geometry.Combine([rings[touching]], [ringVoids], member.Ring, CombineOperation.Add);
                voids[touching] = filled.Failure == CombineFailure.None && filled.Voids.Count > 0
                    ? KeepInside(joined, filled.Voids[0])
                    : KeepInside(joined, ringVoids);
            }

            rings[touching] = joined;
            return null;
        }

        var combined = geometry.Combine(rings, voids, member.Ring, CombineOperation.Add);
        if (combined.Failure != CombineFailure.None)
        {
            return $"{member.Name} can't be added to the outline.";
        }

        Replace(rings, voids, combined);
        return null;
    }

    private string? Remove(
        List<IReadOnlyList<GeoPoint>> rings, List<IReadOnlyList<IReadOnlyList<GeoPoint>>> voids, SiteBoundaryMember member)
    {
        var touching = IndexOfRingNear(rings, member.Ring);
        if (touching >= 0 && member.Relation == SiteBoundaryMemberRelation.Connected)
        {
            var cut = geometry.Cut(rings[touching], member.Ring);
            voids[touching] = KeepInside(cut, voids[touching]);
            rings[touching] = cut;
            return null;
        }

        var combined = geometry.Combine(rings, voids, member.Ring, CombineOperation.Cut);

        // A building that stands wholly in a void is already outside the site: nothing to take out.
        if (combined.Failure == CombineFailure.NothingChanged)
        {
            return null;
        }

        if (combined.Failure == CombineFailure.NothingLeft)
        {
            return $"Taking out {member.Name} would leave nothing of the outline.";
        }

        if (combined.Failure != CombineFailure.None)
        {
            return $"{member.Name} can't be taken out of the outline.";
        }

        Replace(rings, voids, combined);
        return null;
    }

    /// <summary>The voids still lying wholly inside <paramref name="ring"/>; one the new edge reaches is no longer a void.</summary>
    private IReadOnlyList<IReadOnlyList<GeoPoint>> KeepInside(
        IReadOnlyList<GeoPoint> ring, IReadOnlyList<IReadOnlyList<GeoPoint>> candidates)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        return [.. candidates.Where(v => geometry.ValidateVoids(ring, [v]).Result == RingValidationResult.Ok)];
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

    private static void Replace(
        List<IReadOnlyList<GeoPoint>> rings, List<IReadOnlyList<IReadOnlyList<GeoPoint>>> voids, CombineResult next)
    {
        rings.Clear();
        rings.AddRange(next.Rings);
        voids.Clear();
        voids.AddRange(next.Rings.Select((_, i) => i < next.Voids.Count ? next.Voids[i] : []));
    }

    private static IReadOnlyList<GeoPoint> Closed(IReadOnlyList<GeoPoint> ring) =>
        ring.Count > 1 && ring[0] == ring[^1] ? ring : [.. ring, ring[0]];
}
