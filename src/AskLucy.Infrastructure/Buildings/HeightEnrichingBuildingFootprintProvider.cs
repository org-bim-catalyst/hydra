using AskLucy.Application.Buildings;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Buildings;

internal static partial class HeightEnrichingBuildingFootprintProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Measured building heights unavailable for ({Latitude}, {Longitude}); keeping assumed heights")]
    public static partial void HeightsUnavailable(ILogger logger, Exception exception, double latitude, double longitude);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Measured heights for ({Latitude}, {Longitude}) replaced {EnrichedCount} of {AssumedCount} assumed building heights, added {PartCount} taller parts and {GapCount} buildings no footprint source had ({DroppedCount} dropped at the building limit)")]
    public static partial void Enriched(ILogger logger, double latitude, double longitude, int enrichedCount, int assumedCount, int partCount, int gapCount, int droppedCount);
}

/// <summary>
/// specs/075, reworked by specs/076 — the registered <see cref="IBuildingFootprintProvider"/>: the
/// footprint sources' result, completed with measured roof heights from a
/// <see cref="BuildingHeightMap"/>. Footprints and heights come from different vendors because the
/// best outline for the basemap (Google's own map) and the best height (Esri's Vantor
/// photogrammetry) are different vendors; neither has both.
///
/// <para>
/// Heights are read cell by cell rather than per source building, because a source building can be
/// a whole complex (BurJuman's mall and tower are one Esri feature). Three things happen:
/// </para>
/// <list type="number">
/// <item>An ASSUMED footprint whose cells are at least half measured takes the median measured
/// height. The median, not the maximum: a mall with a tower is mostly mall. Known heights are never
/// overwritten — a recorded height is a building-specific fact, while a raster joined by position
/// could belong to a neighbour where the sources disagree.</item>
/// <item>Where part of a footprint stands clearly taller than the footprint's height, that part is
/// added as a building of its own, repeated up to <see cref="MaxSetbackDepth"/> times for towers
/// with setbacks. This is what gives BurJuman's tower its 105 m shadow while the mall around it
/// casts a 25 m one.</item>
/// <item>Measured buildings no footprint covers are added from the raster itself, once cells near
/// an existing footprint are set aside so that footprints misaligned by a few metres do not leave a
/// ghost building beside themselves.</item>
/// </list>
///
/// <para>
/// Heights are an enhancement, not a requirement: the height search runs alongside the footprint
/// search, and its failure is logged and the footprints are returned with their assumed heights,
/// so an Esri outage never takes the solar analysis down with it.
/// </para>
/// </summary>
internal sealed class HeightEnrichingBuildingFootprintProvider(
    [FromKeyedServices(HeightEnrichingBuildingFootprintProvider.FootprintsKey)] IBuildingFootprintProvider footprints,
    IBuildingHeightSource heights,
    IOptions<BuildingRetrievalOptions> retrievalOptions,
    ILogger<HeightEnrichingBuildingFootprintProvider> logger) : IBuildingFootprintProvider
{
    public const string FootprintsKey = "footprints";

    /// <summary>
    /// Footprints kept whole may extend past the radius, so heights are fetched a little further
    /// out than the footprints themselves.
    /// </summary>
    private const int HeightSearchMarginMetres = 100;

    /// <summary>The share of a footprint's cells that must be measured before its height is replaced.</summary>
    private const double MinimumCoverage = 0.5;

    /// <summary>A part is "clearly taller" by this much or <see cref="MinimumStepFraction"/> of the base, whichever is more.</summary>
    private const double MinimumStepMetres = 3.0;

    private const double MinimumStepFraction = 0.15;

    private const int MaxSetbackDepth = 3;

    private const double MinimumPartAreaSquareMetres = 20.0;

    private const double MinimumGapBuildingAreaSquareMetres = 30.0;

    /// <summary>How far around a footprint measured cells are treated as that footprint's (~6 m misalignment between sources).</summary>
    private const int ClaimMarginCells = 2;

    public async Task<BuildingFootprintResult> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default)
    {
        var heightsTask = SearchHeightsAsync(center, radiusMetres + HeightSearchMarginMetres, cancellationToken);
        BuildingFootprintResult result;
        try
        {
            result = await footprints.SearchAsync(center, radiusMetres, cancellationToken);
        }
        finally
        {
            // Never leave the height search running unobserved, whatever the footprints did.
            await heightsTask;
        }

        return Enrich(result, await heightsTask, center, radiusMetres);
    }

    private BuildingFootprintResult Enrich(BuildingFootprintResult result, BuildingHeightMap map, GeoPoint center, int radiusMetres)
    {
        if (!map.HasMeasurements) return result;

        var grid = map.Grid;
        var cellsOf = result.Buildings.Select(b => HeightMapRegions.CellsUnder(grid, b.Ring)).ToList();

        // A cell under several footprints belongs to the smallest, so a tower mapped inside its
        // podium yields its taller part once, as itself, not again as the podium's.
        var owner = new Dictionary<(int X, int Y), int>();
        foreach (var i in Enumerable.Range(0, cellsOf.Count).OrderBy(i => cellsOf[i].Count))
        {
            foreach (var cell in cellsOf[i]) owner.TryAdd(cell, i);
        }

        var buildings = new List<BuildingFootprint>(result.Buildings.Count);
        var parts = new List<BuildingFootprint>();
        var enrichedCount = 0;
        for (var i = 0; i < result.Buildings.Count; i++)
        {
            var building = result.Buildings[i];
            var measured = cellsOf[i].Where(c => !float.IsNaN(map[c.X, c.Y])).ToList();
            if (building.HeightProvenance == BuildingHeightProvenance.Assumed
                && measured.Count > 0 && measured.Count >= MinimumCoverage * cellsOf[i].Count)
            {
                building = building with { HeightMetres = HeightMapRegions.Median(map, measured), HeightProvenance = BuildingHeightProvenance.Known };
                enrichedCount++;
            }

            buildings.Add(building);
            if (building.HeightProvenance == BuildingHeightProvenance.Known)
            {
                var own = measured.Where(c => owner[c] == i).ToList();
                AddTallerParts(map, building.Id, own, building.HeightMetres, depth: 1, parts);
            }
        }

        var gaps = GapBuildings(map, owner.Keys, center, radiusMetres);
        if (!buildings.Any(b => b.IsSiteBuilding))
        {
            var site = SiteBuildingLocator.FindIndex(gaps.Select(g => g.Ring).ToList(), center);
            if (site >= 0) gaps[site] = gaps[site] with { IsSiteBuilding = true };
        }

        // Taller parts first: they carry the longest shadows. Gap buildings nearest the site next,
        // with the site's own building (if one was found among them) guaranteed a place.
        var extras = parts
            .Concat(gaps.OrderByDescending(g => g.IsSiteBuilding).ThenBy(g => GeometryMath.DistanceMeters(Centroid(g.Ring), center)))
            .ToList();
        var room = Math.Max(0, retrievalOptions.Value.MaxBuildingCount - buildings.Count);
        var dropped = Math.Max(0, extras.Count - room);
        buildings.AddRange(extras.Take(room));

        var assumedCount = result.Buildings.Count(b => b.HeightProvenance == BuildingHeightProvenance.Assumed);
        HeightEnrichingBuildingFootprintProviderLog.Enriched(
            logger, center.Latitude, center.Longitude, enrichedCount, assumedCount, parts.Count, gaps.Count, dropped);
        return result with { Buildings = buildings, Limited = result.Limited || dropped > 0 };
    }

    private static void AddTallerParts(
        BuildingHeightMap map, string parentId, IReadOnlyCollection<(int X, int Y)> cells, double baseHeight, int depth, List<BuildingFootprint> parts)
    {
        var threshold = baseHeight + Math.Max(MinimumStepMetres, MinimumStepFraction * baseHeight);
        var taller = cells.Where(c => map[c.X, c.Y] > threshold);
        var index = 0;
        foreach (var component in HeightMapRegions.Components(taller, MinimumCells(map.Grid, MinimumPartAreaSquareMetres)))
        {
            if (HeightMapRegions.Outline(map.Grid, component) is not { } ring) continue;

            var part = new BuildingFootprint(
                $"{parentId}_part{index++}", ring, HeightMapRegions.Median(map, component), BuildingHeightProvenance.Known, string.Empty, IsSiteBuilding: false);
            parts.Add(part);
            if (depth < MaxSetbackDepth) AddTallerParts(map, part.Id, component, part.HeightMetres, depth + 1, parts);
        }
    }

    private static List<BuildingFootprint> GapBuildings(BuildingHeightMap map, IEnumerable<(int X, int Y)> claimedCells, GeoPoint center, int radiusMetres)
    {
        var grid = map.Grid;
        var claimed = new bool[grid.Width, grid.Height];
        foreach (var (x, y) in claimedCells) claimed[x, y] = true;
        claimed = HeightMapRegions.Dilate(claimed, ClaimMarginCells);

        var candidates = new bool[grid.Width, grid.Height];
        for (var x = 0; x < grid.Width; x++)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                candidates[x, y] = !claimed[x, y] && !float.IsNaN(map[x, y]);
            }
        }

        candidates = HeightMapRegions.Open(candidates);
        var cells = new List<(int X, int Y)>();
        for (var x = 0; x < grid.Width; x++)
        {
            for (var y = 0; y < grid.Height; y++)
            {
                if (candidates[x, y]) cells.Add((x, y));
            }
        }

        var gaps = new List<BuildingFootprint>();
        foreach (var component in HeightMapRegions.Components(cells, MinimumCells(grid, MinimumGapBuildingAreaSquareMetres)))
        {
            var centre = grid.FromGrid(component.Average(c => c.X + 0.5), component.Average(c => c.Y + 0.5));
            if (GeometryMath.DistanceMeters(centre, center) > radiusMetres) continue;
            if (HeightMapRegions.Outline(grid, component) is not { } ring) continue;

            gaps.Add(new BuildingFootprint(
                $"esri_{gaps.Count}", ring, HeightMapRegions.Median(map, component), BuildingHeightProvenance.Known, string.Empty, IsSiteBuilding: false));
        }

        return gaps;
    }

    private static int MinimumCells(GeoGrid grid, double areaSquareMetres) =>
        Math.Max(1, (int)Math.Ceiling(areaSquareMetres / (grid.CellMetres * grid.CellMetres)));

    private static GeoPoint Centroid(IReadOnlyList<GeoPoint> ring) =>
        new(ring.Average(p => p.Latitude), ring.Average(p => p.Longitude));

    /// <summary>Never throws except for the caller's own cancellation — see the class remarks.</summary>
    private async Task<BuildingHeightMap> SearchHeightsAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken)
    {
        try
        {
            return await heights.SearchAsync(center, radiusMetres, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            HeightEnrichingBuildingFootprintProviderLog.HeightsUnavailable(logger, ex, center.Latitude, center.Longitude);
            return BuildingHeightMap.Empty;
        }
    }
}
