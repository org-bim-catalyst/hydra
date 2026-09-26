using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Buildings;

/// <summary>
/// specs/076 — combines footprints from several sources into one set without drawing any building
/// twice. The highest-priority source's footprints are kept as they are; a lower-priority footprint
/// is compared with them on a 1 m grid and either
/// <list type="bullet">
/// <item>fills a gap, when little of it overlaps a kept footprint — a building the better source
/// missed; or</item>
/// <item>is the same building seen by another source, when it does overlap. It is then not drawn,
/// but a height it knows can be passed to the kept footprint it covers.</item>
/// </list>
/// Two outlines of one building never coincide exactly (the sources sit up to ~6 m apart), which is
/// why this compares overlap rather than equality, and why a counterpart is never drawn alongside
/// the kept footprint: each would cast its own shadow, slightly offset (specs/052 research D13).
/// </summary>
internal static class BuildingFootprintConflation
{
    /// <summary>Below this share of its area overlapping kept footprints, a footprint is a new building.</summary>
    internal const double GapFillMaximumOverlap = 0.3;

    /// <summary>A counterpart passes its height on only to a kept footprint it covers at least this much of.</summary>
    internal const double HeightTransferMinimumCover = 0.5;

    private const double CellMetres = 1.0;

    /// <summary>Footprints are kept whole past the radius, so the grid reaches this much further.</summary>
    private const double GridMarginMetres = 150.0;

    public sealed record Outcome(
        IReadOnlyList<BuildingFootprint> Buildings, int GapFillCount, int HeightTransferCount, bool DroppedAtLimit);

    /// <param name="sources">Each source's footprints, highest priority first. The first is kept whole.</param>
    public static Outcome Conflate(IReadOnlyList<IReadOnlyList<BuildingFootprint>> sources, GeoPoint center, int radiusMetres, int maxBuildingCount)
    {
        var grid = GeoGrid.Around(center, radiusMetres + GridMarginMetres, CellMetres);
        var owner = new int[grid.Width * grid.Height];
        Array.Fill(owner, -1);

        var kept = new List<BuildingFootprint>();
        var keptCells = new List<int>();
        var siteCandidates = new List<(BuildingFootprint Footprint, int Area, int KeptIndex)>();
        var gapFillCount = 0;
        var heightTransferCount = 0;
        var droppedAtLimit = false;

        void Keep(BuildingFootprint footprint, List<(int X, int Y)> cells)
        {
            var index = kept.Count;
            kept.Add(footprint);
            var claimed = 0;
            foreach (var (x, y) in cells)
            {
                ref var cellOwner = ref owner[(y * grid.Width) + x];
                if (cellOwner >= 0) continue;
                cellOwner = index;
                claimed++;
            }

            keptCells.Add(claimed);
        }

        foreach (var footprint in sources[0])
        {
            var cells = HeightMapRegions.CellsUnder(grid, footprint.Ring);
            if (footprint.IsSiteBuilding) siteCandidates.Add((footprint, cells.Count, kept.Count));
            Keep(footprint, cells);
        }

        foreach (var source in sources.Skip(1))
        {
            // Compared against what was kept before this source, so two footprints from one source
            // never count as each other's counterpart.
            var keptBefore = kept.Count;
            foreach (var footprint in source)
            {
                var cells = HeightMapRegions.CellsUnder(grid, footprint.Ring);
                if (cells.Count == 0) continue;

                var overlaps = new Dictionary<int, int>();
                foreach (var (x, y) in cells)
                {
                    var cellOwner = owner[(y * grid.Width) + x];
                    if (cellOwner >= 0 && cellOwner < keptBefore) overlaps[cellOwner] = overlaps.GetValueOrDefault(cellOwner) + 1;
                }

                if (overlaps.Values.Sum() < GapFillMaximumOverlap * cells.Count)
                {
                    if (kept.Count >= maxBuildingCount)
                    {
                        droppedAtLimit = true;
                        continue;
                    }

                    if (footprint.IsSiteBuilding) siteCandidates.Add((footprint, cells.Count, kept.Count));
                    Keep(footprint, cells);
                    gapFillCount++;
                    continue;
                }

                if (footprint.IsSiteBuilding)
                {
                    siteCandidates.Add((footprint, cells.Count, overlaps.MaxBy(o => o.Value).Key));
                }

                foreach (var (index, count) in overlaps)
                {
                    if (count < HeightTransferMinimumCover * keptCells[index]) continue;
                    if (TransferHeight(kept[index], footprint) is not { } improved) continue;
                    kept[index] = improved;
                    heightTransferCount++;
                }
            }
        }

        return new Outcome(ResolveSiteBuilding(kept, siteCandidates), gapFillCount, heightTransferCount, droppedAtLimit);
    }

    /// <summary>
    /// A kept footprint takes a counterpart's height only when that is better information: a
    /// recorded height over any assumption, or a storey-derived assumption over the bare default.
    /// A recorded height of the kept footprint's own is never replaced.
    /// </summary>
    private static BuildingFootprint? TransferHeight(BuildingFootprint kept, BuildingFootprint counterpart)
    {
        if (kept.HeightProvenance != BuildingHeightProvenance.Assumed) return null;
        if (counterpart.HeightProvenance == BuildingHeightProvenance.Known)
            return kept with { HeightMetres = counterpart.HeightMetres, HeightProvenance = BuildingHeightProvenance.Known };
        if (AssumedBuildingHeight.IsBareDefault(kept) && !AssumedBuildingHeight.IsBareDefault(counterpart))
            return kept with { HeightMetres = counterpart.HeightMetres };
        return null;
    }

    /// <summary>
    /// Every source picks its own site building, and they can disagree: at BurJuman the rendered map
    /// yields a 19 m² fragment under the site point while Overture and OSM have the ~600 m² tower.
    /// The largest candidate wins — the fragment is an artefact, the building is not. A winner that
    /// was not kept (a counterpart) passes the role to the kept footprint it overlaps most. The site
    /// building is moved to the front so it survives any later trimming.
    /// </summary>
    private static List<BuildingFootprint> ResolveSiteBuilding(
        List<BuildingFootprint> kept, List<(BuildingFootprint Footprint, int Area, int KeptIndex)> candidates)
    {
        var site = candidates.Count == 0 ? -1 : candidates.MaxBy(c => c.Area).KeptIndex;
        var buildings = new List<BuildingFootprint>(kept.Count);
        if (site >= 0) buildings.Add(kept[site] with { IsSiteBuilding = true });
        for (var i = 0; i < kept.Count; i++)
        {
            if (i == site) continue;
            buildings.Add(kept[i].IsSiteBuilding ? kept[i] with { IsSiteBuilding = false } : kept[i]);
        }

        return buildings;
    }
}
