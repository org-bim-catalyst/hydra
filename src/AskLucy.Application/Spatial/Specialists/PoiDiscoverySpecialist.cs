using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.SiteBoundaries;

namespace AskLucy.Application.Spatial.Specialists;

/// <summary>
/// The first spatial specialist (FR-029): finds every place of one category inside the resolved
/// study area and returns them as a marker layer draft. Retrieval only — it asks no questions,
/// draws nothing, and leaves keep/replace and narration to the coordinator.
/// <para>
/// The category resolution, radius/bounds handling and provider-failure semantics are moved here
/// from the retired POI capability, so specs 090's accepted behaviour is preserved rather than
/// reimplemented.
/// </para>
/// </summary>
public sealed class PoiDiscoverySpecialist(
    IPoiQueryProvider poiQueryProvider,
    PoiCategoryCatalog categoryCatalog) : ISpatialSpecialist
{
    public const string SourceId = "overpass";
    public const string SourceDisplayName = "OpenStreetMap";

    public string OperationKind => SpatialOperationKinds.PoiDiscovery;

    public async Task<SpatialSpecialistOutcome> ExecuteAsync(
        SpatialOperation operation,
        SpatialExecutionContext context,
        CancellationToken cancellationToken)
    {
        var categoryPhrase = operation.Targets
            .FirstOrDefault(target => !string.IsNullOrWhiteSpace(target.Category))?.Category;

        if (!categoryCatalog.TryResolve(categoryPhrase, out var category))
        {
            return SpatialSpecialistOutcome.Failed(
                $"I can't tell which place type to map — I know these: {categoryCatalog.DescribeSupported()}.");
        }

        if (context.StudyArea is not { } area)
        {
            return SpatialSpecialistOutcome.Failed("I don't know which area to search.");
        }

        var bounds = area.Viewport is null
            ? null
            : new PoiBoundingBox(
                Math.Min(area.Viewport.SouthwestLat, area.Viewport.NortheastLat),
                Math.Min(area.Viewport.SouthwestLng, area.Viewport.NortheastLng),
                Math.Max(area.Viewport.SouthwestLat, area.Viewport.NortheastLat),
                Math.Max(area.Viewport.SouthwestLng, area.Viewport.NortheastLng));

        IReadOnlyList<PoiFeature> features;
        try
        {
            features = await poiQueryProvider.QueryAsync(
                new PoiQuery(area.Center, (int)area.RadiusMeters, category, Bounds: bounds, AreaName: area.DisplayName),
                cancellationToken);
        }
        catch (BoundaryProviderUnavailableException ex)
        {
            // Expected, ordinary failure: the free public Overpass endpoints saturate. Reported as
            // a failure so Lucy says the lookup failed — never as an empty result, which would
            // claim the area has no schools.
            return SpatialSpecialistOutcome.Failed(
                $"The map data service was unavailable while searching {area.DisplayName} ({ex.Message}).");
        }

        var coverage = new[] { DataSourceCoverage.Complete(SourceId, SourceDisplayName) };

        if (features.Count == 0)
        {
            return SpatialSpecialistOutcome.Empty(coverage);
        }

        var layer = new SpatialLayerDraft(
            LayerId: $"{operation.OperationId}:{category.Key}",
            SourceOperationId: operation.OperationId,
            Name: $"{category.DisplayName} in {area.DisplayName}",
            LegendLabel: category.DisplayName,
            Kind: SpatialLayerKind.Markers,
            Markers: features
                .OrderBy(feature => feature.Name, StringComparer.OrdinalIgnoreCase)
                .Select(feature => new SpatialMarker(feature.Id, feature.Name, feature.Latitude, feature.Longitude))
                .ToList(),
            Style: new SpatialLayerStyle(category.Color, category.MarkerShape),
            Attributes: new Dictionary<string, string> { ["category"] = category.Key });

        return new SpatialSpecialistOutcome(SpatialOperationStatus.Succeeded, [layer], coverage);
    }
}
