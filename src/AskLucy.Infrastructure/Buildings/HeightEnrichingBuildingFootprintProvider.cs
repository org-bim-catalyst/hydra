using AskLucy.Application.Buildings;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Buildings;

internal static partial class HeightEnrichingBuildingFootprintProviderLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Measured building heights unavailable for ({Latitude}, {Longitude}); keeping assumed heights")]
    public static partial void HeightsUnavailable(ILogger logger, Exception exception, double latitude, double longitude);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Measured heights for ({Latitude}, {Longitude}) replaced {EnrichedCount} of {AssumedCount} assumed building heights ({MeasuredCount} measurements)")]
    public static partial void Enriched(ILogger logger, double latitude, double longitude, int enrichedCount, int assumedCount, int measuredCount);
}

/// <summary>
/// specs/075 — the registered <see cref="IBuildingFootprintProvider"/>: the footprint chain's
/// result, with every ASSUMED height replaced by a measured one where a measurement falls inside
/// the footprint. Footprints and heights come from different sources because the best outline
/// for the basemap (rendered from Google's own map) and the best height (Esri's Vantor
/// photogrammetry) are different vendors; neither has both.
///
/// <para>
/// Known heights are never overwritten — a recorded height tag is a building-specific fact, while
/// a measurement joined by position could belong to a neighbour when footprints disagree. When a
/// footprint contains several measurements (one rendered block covering several OSM buildings),
/// the tallest wins: the tallest part is what casts the longest shadow.
/// </para>
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
    ILogger<HeightEnrichingBuildingFootprintProvider> logger) : IBuildingFootprintProvider
{
    public const string FootprintsKey = "footprints";

    /// <summary>
    /// Footprints kept whole may extend past the radius, so heights are fetched a little further
    /// out than the footprints themselves.
    /// </summary>
    private const int HeightSearchMarginMetres = 100;

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

        var measured = await heightsTask;
        return Enrich(result, measured, center);
    }

    private BuildingFootprintResult Enrich(BuildingFootprintResult result, IReadOnlyList<MeasuredBuildingHeight> measured, GeoPoint center)
    {
        var assumedCount = result.Buildings.Count(b => b.HeightProvenance == BuildingHeightProvenance.Assumed);
        if (measured.Count == 0 || assumedCount == 0) return result;

        var enrichedCount = 0;
        var buildings = result.Buildings.Select(building =>
        {
            if (building.HeightProvenance != BuildingHeightProvenance.Assumed) return building;

            var (minLat, minLon, maxLat, maxLon) = GeometryMath.BoundingBox(building.Ring);
            var tallest = measured
                .Where(m => m.Location.Latitude >= minLat && m.Location.Latitude <= maxLat
                    && m.Location.Longitude >= minLon && m.Location.Longitude <= maxLon
                    && GeometryMath.Contains(building.Ring, m.Location))
                .Select(m => (double?)m.HeightMetres)
                .Max();
            if (tallest is not { } height) return building;

            enrichedCount++;
            return building with { HeightMetres = height, HeightProvenance = BuildingHeightProvenance.Known };
        }).ToList();

        HeightEnrichingBuildingFootprintProviderLog.Enriched(logger, center.Latitude, center.Longitude, enrichedCount, assumedCount, measured.Count);
        return result with { Buildings = buildings };
    }

    /// <summary>Never throws except for the caller's own cancellation — see the class remarks.</summary>
    private async Task<IReadOnlyList<MeasuredBuildingHeight>> SearchHeightsAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken)
    {
        try
        {
            return await heights.SearchAsync(center, radiusMetres, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            HeightEnrichingBuildingFootprintProviderLog.HeightsUnavailable(logger, ex, center.Latitude, center.Longitude);
            return [];
        }
    }
}
