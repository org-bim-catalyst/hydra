using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Buildings;

internal static partial class CompositeBuildingFootprintProviderLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Building footprints for ({Latitude}, {Longitude}) supplied by {Source} ({Count} buildings)")]
    public static partial void ResultSupplied(ILogger logger, double latitude, double longitude, BuildingFootprintSource source, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Building-footprint source {Index} of {Count} unavailable for ({Latitude}, {Longitude}); trying the next")]
    public static partial void SourceUnavailable(ILogger logger, Exception exception, int index, int count, double latitude, double longitude);
}

/// <summary>
/// specs/053-rendered-building-footprints contracts/footprint-source-arbitration.md — the
/// footprint chain behind the registered <see cref="IBuildingFootprintProvider"/>. A decorator over
/// an ordered list of inner providers, not an <c>if</c> inside one, so each source stays
/// single-purpose (§2.II SRP) and a further source is an added implementation, not an edit to
/// working code (OCP) — specs/075 added Overture exactly that way.
///
/// <para>
/// Rendered is primary because it is the source that works where this platform's users are — three
/// published alternatives were evaluated (spec.md Downstream) and all three exclude the Gulf.
/// Overture and Overpass are kept as fallbacks because they fail for UNRELATED reasons (styling/
/// coverage vs. a static archive vs. load), which is what makes the chain worth more than any one.
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
internal sealed class CompositeBuildingFootprintProvider : IBuildingFootprintProvider
{
    /// <summary>
    /// Depends on the INTERFACE, disambiguated via keyed DI, rather than the concrete provider
    /// types directly. Corrected during implementation: the concrete-type constructor this contract
    /// originally specified cannot be unit-tested, because the providers are <c>sealed</c> and
    /// NSubstitute cannot proxy a sealed class. Keyed <see cref="IBuildingFootprintProvider"/> is
    /// both fully fakeable and better dependency inversion (constitution §2.V) — this class never
    /// needs to know which concrete types exist, only their order.
    /// </summary>
    public const string RenderedKey = "rendered";
    public const string OvertureKey = "overture";
    public const string OsmKey = "osm";

    private readonly IReadOnlyList<IBuildingFootprintProvider> _providers;
    private readonly ILogger<CompositeBuildingFootprintProvider> _logger;

    /// <summary>
    /// specs/075 — rendered, then Overture, then OSM. Overture sits between the two because it
    /// already contains OSM's buildings plus machine-detected ones where OSM has none, so OSM on
    /// its own is only worth asking when Overture's archive can't be reached.
    /// </summary>
    public CompositeBuildingFootprintProvider(
        [FromKeyedServices(RenderedKey)] IBuildingFootprintProvider rendered,
        [FromKeyedServices(OvertureKey)] IBuildingFootprintProvider overture,
        [FromKeyedServices(OsmKey)] IBuildingFootprintProvider osm,
        ILogger<CompositeBuildingFootprintProvider> logger)
        : this([rendered, overture, osm], logger)
    {
    }

    internal CompositeBuildingFootprintProvider(
        IReadOnlyList<IBuildingFootprintProvider> providers,
        ILogger<CompositeBuildingFootprintProvider> logger)
    {
        if (providers.Count == 0) throw new ArgumentException("At least one provider is required.", nameof(providers));
        _providers = providers;
        _logger = logger;
    }

    public async Task<BuildingFootprintResult> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default)
    {
        BuildingFootprintResult? lastResult = null;
        for (var i = 0; i < _providers.Count; i++)
        {
            try
            {
                lastResult = await _providers[i].SearchAsync(center, radiusMetres, cancellationToken);
            }
            catch (BuildingProviderUnavailableException ex) when (i < _providers.Count - 1)
            {
                // A failing source is not a feature failure (FR-012) — it is the ordinary path to
                // the next one. The LAST source failing is deliberately not caught: it rethrows so
                // the existing 503 Problem Details mapping still applies unchanged (FR-013).
                CompositeBuildingFootprintProviderLog.SourceUnavailable(_logger, ex, i + 1, _providers.Count, center.Latitude, center.Longitude);
                continue;
            }

            if (lastResult.Buildings.Count > 0)
            {
                // FR-014 — results are never merged. The sources' polygons do not coincide, so
                // merging would draw every building twice, slightly offset, each casting its own
                // shadow — the "ghost duplicate" failure specs/052 already solved once (research D13).
                CompositeBuildingFootprintProviderLog.ResultSupplied(_logger, center.Latitude, center.Longitude, lastResult.Source, lastResult.Buildings.Count);
                return lastResult;
            }

            // Succeeded but found nothing — fall through rather than accepting an empty result as
            // final, in case a later source has data this one did not.
        }

        // Every source empty is a legitimate success — Source = none, since no source actually
        // supplied anything (contracts/footprint-source-arbitration.md). lastResult is set: only
        // the last source can end the loop without one, and its failure rethrows above.
        var result = lastResult! with { Source = BuildingFootprintSource.None };
        CompositeBuildingFootprintProviderLog.ResultSupplied(_logger, center.Latitude, center.Longitude, result.Source, result.Buildings.Count);
        return result;
    }
}
