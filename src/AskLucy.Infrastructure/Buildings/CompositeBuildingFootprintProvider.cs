using System.Runtime.ExceptionServices;
using AskLucy.Application.Buildings;
using AskLucy.Domain.SiteBoundaries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Buildings;

internal static partial class CompositeBuildingFootprintProviderLog
{
    [LoggerMessage(Level = LogLevel.Information,
        Message = "Building footprints for ({Latitude}, {Longitude}) supplied by {Source} ({Count} buildings, {GapFillCount} added from other sources, {HeightTransferCount} heights taken from other sources)")]
    public static partial void ResultSupplied(ILogger logger, double latitude, double longitude, BuildingFootprintSource source, int count, int gapFillCount, int heightTransferCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Building-footprint source {Index} of {Count} unavailable for ({Latitude}, {Longitude})")]
    public static partial void SourceUnavailable(ILogger logger, Exception exception, int index, int count, double latitude, double longitude);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Building-footprint source {Index} of {Count} did not answer for ({Latitude}, {Longitude}) within {Budget} of the preferred source and was cancelled; its buildings are missing from this result")]
    public static partial void SourceTooSlow(ILogger logger, int index, int count, double latitude, double longitude, TimeSpan budget);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Building footprints for ({Latitude}, {Longitude}) reached the {Limit}-building limit; further buildings from other sources were dropped")]
    public static partial void LimitReached(ILogger logger, double latitude, double longitude, int limit);
}

/// <summary>
/// specs/053-rendered-building-footprints, reworked by specs/076 — the footprint sources behind the
/// registered <see cref="IBuildingFootprintProvider"/>. A decorator over an ordered list of inner
/// providers, not an <c>if</c> inside one, so each source stays single-purpose (§2.II SRP) and a
/// further source is an added implementation, not an edit to working code (OCP).
///
/// <para>
/// All sources are asked at once. The highest-priority source with any buildings supplies the
/// footprints; the others fill its gaps and pass on heights it lacks
/// (<see cref="BuildingFootprintConflation"/>). Rendered is first because it traces the basemap the
/// user is looking at; Overture next because it holds OSM's buildings plus machine-detected ones;
/// OSM last. Each misses buildings the others have — Google's map leaves out whole complexes that
/// Overture has and vice versa — which is why they are combined rather than chosen between.
/// </para>
///
/// <para>
/// A failing source is logged and the others carry on; only when every source fails does the
/// failure reach the caller (as the 503 it always was). Lower-priority sources get
/// <see cref="BuildingConflationOptions.StragglerBudget"/> after the preferred one has answered and
/// are then cancelled, so a slow gap-filler never holds up buildings already in hand.
/// </para>
///
/// <para>
/// Holds no cache of its own (research D9) — each inner provider caches its own result, so a
/// source that was slow once is used again as soon as its cache is warm.
/// </para>
/// </summary>
internal sealed class CompositeBuildingFootprintProvider : IBuildingFootprintProvider
{
    /// <summary>
    /// Depends on the INTERFACE, disambiguated via keyed DI, rather than the concrete provider
    /// types directly: the providers are <c>sealed</c> and NSubstitute cannot proxy a sealed class.
    /// Keyed <see cref="IBuildingFootprintProvider"/> is both fully fakeable and better dependency
    /// inversion (constitution §2.V) — this class never needs to know which concrete types exist,
    /// only their order.
    /// </summary>
    public const string RenderedKey = "rendered";
    public const string OvertureKey = "overture";
    public const string OsmKey = "osm";

    private readonly IReadOnlyList<IBuildingFootprintProvider> _providers;
    private readonly BuildingConflationOptions _options;
    private readonly BuildingRetrievalOptions _retrievalOptions;
    private readonly ILogger<CompositeBuildingFootprintProvider> _logger;

    public CompositeBuildingFootprintProvider(
        [FromKeyedServices(RenderedKey)] IBuildingFootprintProvider rendered,
        [FromKeyedServices(OvertureKey)] IBuildingFootprintProvider overture,
        [FromKeyedServices(OsmKey)] IBuildingFootprintProvider osm,
        IOptions<BuildingConflationOptions> options,
        IOptions<BuildingRetrievalOptions> retrievalOptions,
        ILogger<CompositeBuildingFootprintProvider> logger)
        : this([rendered, overture, osm], options.Value, retrievalOptions.Value, logger)
    {
    }

    internal CompositeBuildingFootprintProvider(
        IReadOnlyList<IBuildingFootprintProvider> providers,
        BuildingConflationOptions options,
        BuildingRetrievalOptions retrievalOptions,
        ILogger<CompositeBuildingFootprintProvider> logger)
    {
        if (providers.Count == 0) throw new ArgumentException("At least one provider is required.", nameof(providers));
        _providers = providers;
        _options = options;
        _retrievalOptions = retrievalOptions;
        _logger = logger;
    }

    public async Task<BuildingFootprintResult> SearchAsync(GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default)
    {
        using var stragglers = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runs = _providers.Select(p => RunAsync(p, center, radiusMetres, stragglers.Token, cancellationToken)).ToArray();
        try
        {
            var primary = await FindPrimaryAsync(runs);
            var tooSlow = false;
            if (primary >= 0 && primary < runs.Length - 1)
            {
                var rest = Task.WhenAll(runs.Skip(primary + 1));
                if (await Task.WhenAny(rest, Task.Delay(_options.StragglerBudget, cancellationToken)) != rest)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    tooSlow = true;
                    await stragglers.CancelAsync();
                }
            }

            var outcomes = await Task.WhenAll(runs);
            LogFailures(outcomes, tooSlow, center);
            return primary >= 0 ? Conflate(outcomes, primary, center, radiusMetres) : NothingFound(outcomes, center);
        }
        finally
        {
            // Never leave a source running unobserved, however this method is left.
            await stragglers.CancelAsync();
            try
            {
                await Task.WhenAll(runs);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller's own cancellation, already propagating from the try block.
            }
        }
    }

    /// <summary>The first source, in priority order, with at least one building — or -1 once all have answered without any.</summary>
    private static async Task<int> FindPrimaryAsync(Task<SourceOutcome>[] runs)
    {
        for (var i = 0; i < runs.Length; i++)
        {
            if ((await runs[i]).Result is { Buildings.Count: > 0 }) return i;
        }

        return -1;
    }

    private BuildingFootprintResult Conflate(SourceOutcome[] outcomes, int primary, GeoPoint center, int radiusMetres)
    {
        var primaryResult = outcomes[primary].Result!;
        var sources = new List<IReadOnlyList<BuildingFootprint>> { primaryResult.Buildings };
        sources.AddRange(outcomes.Skip(primary + 1).Where(o => o.Result is not null).Select(o => o.Result!.Buildings));

        var conflated = BuildingFootprintConflation.Conflate(sources, center, radiusMetres, _retrievalOptions.MaxBuildingCount);
        if (conflated.DroppedAtLimit)
            CompositeBuildingFootprintProviderLog.LimitReached(_logger, center.Latitude, center.Longitude, _retrievalOptions.MaxBuildingCount);
        CompositeBuildingFootprintProviderLog.ResultSupplied(
            _logger, center.Latitude, center.Longitude, primaryResult.Source, conflated.Buildings.Count, conflated.GapFillCount, conflated.HeightTransferCount);

        return primaryResult with
        {
            Buildings = conflated.Buildings,
            Limited = primaryResult.Limited || conflated.DroppedAtLimit,
        };
    }

    /// <summary>
    /// No source had a building. If any answered, that is a legitimate empty success with
    /// Source = none (contracts/footprint-source-arbitration.md); if none did, the last source's
    /// failure is rethrown, so every source being down still maps to the 503 it always did.
    /// </summary>
    private BuildingFootprintResult NothingFound(SourceOutcome[] outcomes, GeoPoint center)
    {
        var answered = outcomes.LastOrDefault(o => o.Result is not null)?.Result;
        if (answered is null)
        {
            ExceptionDispatchInfo.Throw(outcomes[^1].Error!);
            throw new InvalidOperationException("Unreachable: ExceptionDispatchInfo.Throw always throws.");
        }

        var result = answered with { Source = BuildingFootprintSource.None };
        CompositeBuildingFootprintProviderLog.ResultSupplied(_logger, center.Latitude, center.Longitude, result.Source, 0, 0, 0);
        return result;
    }

    private void LogFailures(SourceOutcome[] outcomes, bool tooSlow, GeoPoint center)
    {
        for (var i = 0; i < outcomes.Length; i++)
        {
            if (outcomes[i].Error is not { } error) continue;
            if (tooSlow && error is OperationCanceledException)
                CompositeBuildingFootprintProviderLog.SourceTooSlow(_logger, i + 1, outcomes.Length, center.Latitude, center.Longitude, _options.StragglerBudget);
            else
                CompositeBuildingFootprintProviderLog.SourceUnavailable(_logger, error, i + 1, outcomes.Length, center.Latitude, center.Longitude);
        }
    }

    /// <summary>
    /// One source's answer or failure. Only the caller's own cancellation escapes; any other
    /// failure is captured so it can be logged and the remaining sources used (FR-012).
    /// </summary>
    private static async Task<SourceOutcome> RunAsync(
        IBuildingFootprintProvider provider, GeoPoint center, int radiusMetres, CancellationToken sourceToken, CancellationToken callerToken)
    {
        try
        {
            return new SourceOutcome(await provider.SearchAsync(center, radiusMetres, sourceToken), null);
        }
        catch (Exception ex) when (!callerToken.IsCancellationRequested)
        {
            return new SourceOutcome(null, ex);
        }
    }

    private sealed record SourceOutcome(BuildingFootprintResult? Result, Exception? Error);
}
