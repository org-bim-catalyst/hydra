using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Buildings;

internal static partial class CompositeBuildingFootprintProviderLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Building footprints for ({Latitude}, {Longitude}) supplied by {Source} ({Count} buildings)")]
    public static partial void ResultSupplied(ILogger logger, double latitude, double longitude, BuildingFootprintSource source, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rendered building-footprint source unavailable for ({Latitude}, {Longitude}); falling back to OSM")]
    public static partial void PrimaryUnavailable(ILogger logger, Exception exception, double latitude, double longitude);
}

/// <summary>
/// specs/053-rendered-building-footprints contracts/footprint-source-arbitration.md — the
/// registered <see cref="IBuildingFootprintProvider"/>. A decorator over two inner providers, not
/// an <c>if</c> inside one, so each source stays single-purpose (§2.II SRP) and a third source
/// later is an added implementation, not an edit to working code (OCP).
///
/// <para>
/// Rendered is primary because it is the source that works where this platform's users are — three
/// published alternatives were evaluated (spec.md Downstream) and all three exclude the Gulf.
/// Overpass is kept as fallback because the two sources fail for UNRELATED reasons (styling/
/// coverage vs. load), which is what makes the pair worth more than either alone.
/// </para>
///
/// <para>
/// Wraps neither inner provider and holds no cache of its own (research D9) — each inner provider
/// caches its own result independently. A single cache keyed on the arbitration outcome would mean
/// a request that fell back to Overpass once (rendered cache cold) keeps falling back on every
/// later call too, since this class would have no way to know the rendered cache had since warmed
/// up without querying it anyway.
/// </para>
/// </summary>
internal sealed class CompositeBuildingFootprintProvider(
    [FromKeyedServices(CompositeBuildingFootprintProvider.RenderedKey)] IBuildingFootprintProvider primary,
    [FromKeyedServices(CompositeBuildingFootprintProvider.OsmKey)] IBuildingFootprintProvider fallback,
    ILogger<CompositeBuildingFootprintProvider> logger) : IBuildingFootprintProvider
{
    /// <summary>
    /// Depends on the INTERFACE, disambiguated via keyed DI, rather than the two concrete provider
    /// types directly. Corrected during implementation: the concrete-type constructor this contract
    /// originally specified cannot be unit-tested, because both <c>RenderedBuildingFootprintProvider</c>
    /// and <c>OverpassBuildingFootprintProvider</c> are <c>sealed</c> and NSubstitute cannot proxy a
    /// sealed class. Keyed <see cref="IBuildingFootprintProvider"/> is both fully fakeable
    /// (<see cref="Substitute"/> against a plain interface always works) and better dependency
    /// inversion (constitution §2.V) — this class no longer needs to know either concrete type
    /// exists, only that "primary" and "osm" resolve to something implementing the interface.
    /// </summary>
    public const string RenderedKey = "rendered";
    public const string OsmKey = "osm";

    public async Task<BuildingFootprintResult> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default)
    {
        BuildingFootprintResult primaryResult;
        try
        {
            primaryResult = await primary.SearchAsync(center, radiusMetres, cancellationToken);
        }
        catch (BuildingProviderUnavailableException ex)
        {
            // A failing primary is not a feature failure (FR-012) — it is the ordinary path to the
            // fallback. Both failing rethrows below, so the existing 503 Problem Details mapping
            // still applies unchanged (FR-013).
            CompositeBuildingFootprintProviderLog.PrimaryUnavailable(logger, ex, center.Latitude, center.Longitude);
            return await SearchFallbackAsync(center, radiusMetres, cancellationToken);
        }

        if (primaryResult.Buildings.Count > 0)
        {
            CompositeBuildingFootprintProviderLog.ResultSupplied(logger, center.Latitude, center.Longitude, primaryResult.Source, primaryResult.Buildings.Count);
            return primaryResult;
        }

        // Primary succeeded but found nothing — fall through to the fallback rather than accepting
        // an empty rendered result as final, in case Overpass has data the rendering did not.
        return await SearchFallbackAsync(center, radiusMetres, cancellationToken);
    }

    private async Task<BuildingFootprintResult> SearchFallbackAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken)
    {
        // Deliberately NOT caught here: both sources failing must rethrow so the existing 503
        // Problem Details mapping still applies (FR-013) — this call is the last chance to succeed.
        var fallbackResult = await fallback.SearchAsync(center, radiusMetres, cancellationToken);

        // Both empty is a legitimate success — Source = none, not the fallback's own Osm tag, since
        // neither source actually supplied anything (contracts/footprint-source-arbitration.md).
        var result = fallbackResult.Buildings.Count == 0
            ? fallbackResult with { Source = BuildingFootprintSource.None }
            : fallbackResult;

        // FR-014 — results are never merged. The two sources' polygons do not coincide, so merging
        // would draw every building twice, slightly offset, each casting its own shadow — the same
        // "ghost duplicate" failure specs/052 already solved once (its research D13).
        CompositeBuildingFootprintProviderLog.ResultSupplied(logger, center.Latitude, center.Longitude, result.Source, result.Buildings.Count);
        return result;
    }
}
