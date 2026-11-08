using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Density;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Spatial.Density;

/// <summary>Initial density adapter over the existing authorized POI source.</summary>
internal sealed class OverpassDensityAnalysisProvider(
    IPoiQueryProvider poiQueryProvider,
    PoiCategoryCatalog categoryCatalog) : IDensityAnalysisProvider
{
    public IReadOnlyList<string> SupportedDatasetIds => ["buildings", "schools", "hospitals"];

    public async Task<DensityProviderResult> CalculateAsync(DensityAnalysisDefinition definition,
        SpatialExecutionContext context, CancellationToken cancellationToken)
    {
        var categoryKey = definition.Dataset.CategoryKey ?? definition.Dataset.Id.TrimEnd('s');
        if (!categoryCatalog.TryResolve(categoryKey, out var category))
            throw new InvalidOperationException($"No POI category is configured for {definition.Dataset.DisplayName}.");

        var area = definition.StudyArea;
        var features = await poiQueryProvider.QueryAsync(new PoiQuery(area.Center, (int)Math.Min(area.RadiusMeters, 50_000), category,
            AreaName: area.DisplayName), cancellationToken).ConfigureAwait(false);
        if (features.Count == 0)
            return new DensityProviderResult([], new DensityClassificationResult(definition.Classification, [], []),
                [DataSourceCoverage.Complete("overpass", "OpenStreetMap")]);

        // The first provider emits one bounded aggregate cell. Later providers can add finer grids without changing Application contracts.
        var delta = Math.Max(0.001, Math.Min(area.RadiusMeters / 111_000d, 0.25));
        var ring = new[]
        {
            new GeoPoint(area.Center.Latitude - delta, area.Center.Longitude - delta),
            new GeoPoint(area.Center.Latitude - delta, area.Center.Longitude + delta),
            new GeoPoint(area.Center.Latitude + delta, area.Center.Longitude + delta),
            new GeoPoint(area.Center.Latitude + delta, area.Center.Longitude - delta),
            new GeoPoint(area.Center.Latitude - delta, area.Center.Longitude - delta),
        };
        var areaKm2 = Math.PI * Math.Pow(Math.Max(area.RadiusMeters, 1) / 1000d, 2);
        var value = features.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() / areaKm2;
        var classification = new DensityClassificationResult(definition.Classification, [value], ["Observed density"]);
        return new DensityProviderResult([new DensityCell("aggregate", ring, value, definition.Dataset.CanonicalUnit, 0, "Observed density", features.Count)],
            classification, [DataSourceCoverage.Partial("overpass", "OpenStreetMap", "Initial provider reports an aggregate cell; source coverage may be incomplete.")],
            ["Density reflects available mapped features and is not a measure of service need."]);
    }
}
