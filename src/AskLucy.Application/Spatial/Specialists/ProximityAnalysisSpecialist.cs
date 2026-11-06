using AskLucy.Application.PointsOfInterest;
using AskLucy.Application.SiteBoundaries;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Spatial.Specialists;

/// <summary>
/// specs/093 — measures the distance between two feature sets inside the resolved study area and
/// returns the features that satisfy the requested distance rule.
///
/// <para>
/// Retrieval and arithmetic only: it asks no questions, draws nothing and narrates nothing. The
/// coordinator has already interpreted the request (source category, target category, thresholds,
/// unit, within/beyond) and owns keep-or-replace and delivery.
/// </para>
///
/// <para>
/// <b>Distance semantics</b> follow the specification exactly: <c>within</c> is distance ≤ the
/// threshold; <c>beyond</c> is distance &gt; it; distance bands are lower-inclusive and
/// upper-exclusive, with the final band's upper bound included. Adjacent bands therefore never
/// both claim a feature sitting exactly on the shared boundary.
/// </para>
/// </summary>
public sealed class ProximityAnalysisSpecialist(
    IPoiQueryProvider poiQueryProvider,
    PoiCategoryCatalog categoryCatalog) : ISpatialSpecialist
{
    public const string SourceId = "overpass";
    public const string SourceDisplayName = "OpenStreetMap";

    /// <summary>Buffers are drawn for this many targets; more would flood the legend.</summary>
    private const int MaxBufferLayers = 10;

    private static readonly string[] BandColors =
    [
        "#4285F4", "#EA4335", "#34A853", "#FB8C00", "#8E24AA", "#00ACC1",
    ];

    public string OperationKind => SpatialOperationKinds.ProximityAnalysis;

    public async Task<SpatialSpecialistOutcome> ExecuteAsync(
        SpatialOperation operation,
        SpatialExecutionContext context,
        CancellationToken cancellationToken)
    {
        var sourceSelector = FindSelector(operation.Targets, "source");
        var targetSelector = FindSelector(operation.Targets, "target");

        if (sourceSelector is null)
        {
            return SpatialSpecialistOutcome.Failed("I need to know which places to measure from.");
        }

        if (targetSelector is null)
        {
            return SpatialSpecialistOutcome.Failed("I need to know what to measure the distance to.");
        }

        if (!categoryCatalog.TryResolve(sourceSelector.Category, out var sourceCategory))
        {
            return Unsupported(sourceSelector.Category);
        }

        if (!categoryCatalog.TryResolve(targetSelector.Category, out var targetCategory))
        {
            return Unsupported(targetSelector.Category);
        }

        if (context.StudyArea is not { } area)
        {
            return SpatialSpecialistOutcome.Failed("I don't know which area to measure in.");
        }

        var thresholds = operation.Parameters?.DistanceThresholds;
        if (thresholds is not { Count: > 0 } || thresholds[0].UpperBound is not { } firstUpper ||
            !double.IsFinite(firstUpper) || firstUpper <= 0)
        {
            return SpatialSpecialistOutcome.Failed("I need a distance to measure — for example \"within 1 km\".");
        }

        var unit = operation.Parameters?.DistanceUnit ?? "m";
        var toMeters = string.Equals(unit, "km", StringComparison.OrdinalIgnoreCase) ? 1000d : 1d;
        var isBeyond = string.Equals(operation.Parameters?.LayerMode, "beyond", StringComparison.OrdinalIgnoreCase);

        var bounds = area.Viewport is null
            ? null
            : new PoiBoundingBox(
                Math.Min(area.Viewport.SouthwestLat, area.Viewport.NortheastLat),
                Math.Min(area.Viewport.SouthwestLng, area.Viewport.NortheastLng),
                Math.Max(area.Viewport.SouthwestLat, area.Viewport.NortheastLat),
                Math.Max(area.Viewport.SouthwestLng, area.Viewport.NortheastLng));

        var sourceQuery = new PoiQuery(area.Center, (int)area.RadiusMeters, sourceCategory, Bounds: bounds, AreaName: area.DisplayName);
        var targetQuery = new PoiQuery(area.Center, (int)area.RadiusMeters, targetCategory, Bounds: bounds, AreaName: area.DisplayName);

        IReadOnlyList<PoiFeature> sources;
        IReadOnlyList<PoiFeature> targets;
        try
        {
            // Concurrently: each is a country-scale-capable Overpass query, and running them in
            // sequence would double the one cost that already dominates this capability.
            var sourceTask = poiQueryProvider.QueryAsync(sourceQuery, cancellationToken);
            var targetTask = poiQueryProvider.QueryAsync(targetQuery, cancellationToken);
            await Task.WhenAll(sourceTask, targetTask);
            sources = sourceTask.Result;
            targets = targetTask.Result;
        }
        catch (BoundaryProviderUnavailableException ex)
        {
            // Ordinary provider failure — never reported as "no places found".
            return SpatialSpecialistOutcome.Failed(
                $"The map data service was unavailable while measuring in {area.DisplayName} ({ex.Message}).");
        }

        var coverage = new[] { DataSourceCoverage.Complete(SourceId, SourceDisplayName) };
        var warnings = new List<string>();

        if (sources.Count >= sourceQuery.MaxResults)
        {
            warnings.Add($"{sourceCategory.DisplayName} were capped at {sourceQuery.MaxResults} results.");
        }

        if (targets.Count >= targetQuery.MaxResults)
        {
            warnings.Add($"{targetCategory.DisplayName} were capped at {targetQuery.MaxResults} results.");
        }

        if (sources.Count == 0)
        {
            return SpatialSpecialistOutcome.Empty(coverage, warnings.Count > 0 ? warnings : null);
        }

        if (targets.Count == 0)
        {
            warnings.Add($"No {targetCategory.DisplayName.ToLowerInvariant()} were found in {area.DisplayName} to measure from.");
            return SpatialSpecialistOutcome.Empty(coverage, warnings);
        }

        // A distance threshold is in the user's unit; every comparison is in metres.
        var limitMeters = firstUpper * toMeters;
        var bands = thresholds
            .Select(threshold => (Lower: threshold.LowerBound * toMeters, Upper: (threshold.UpperBound ?? threshold.LowerBound) * toMeters))
            .OrderBy(band => band.Upper)
            .ToList();
        var isBanded = !isBeyond && bands.Count > 1;

        var measured = sources
            .Select(source => new
            {
                Feature = source,
                NearestMeters = targets.Min(target => GeometryMath.DistanceMeters(
                    new GeoPoint(source.Latitude, source.Longitude),
                    new GeoPoint(target.Latitude, target.Longitude))),
            })
            .ToList();

        var layers = new List<SpatialLayerDraft>();

        if (isBeyond)
        {
            var qualifying = measured.Where(item => item.NearestMeters > limitMeters).ToList();
            if (qualifying.Count == 0)
            {
                return SpatialSpecialistOutcome.Empty(coverage, warnings.Count > 0 ? warnings : null);
            }

            layers.Add(MarkersLayer(
                operation.OperationId,
                $"more-than-{firstUpper:0.###}{unit}",
                $"{sourceCategory.DisplayName} more than {FormatDistance(firstUpper)} {unit} from {targetCategory.DisplayName} in {area.DisplayName}",
                $"{sourceCategory.DisplayName} > {FormatDistance(firstUpper)} {unit} from {targetCategory.DisplayName}",
                sourceCategory,
                qualifying.Select(item => item.Feature)));
        }
        else if (isBanded)
        {
            for (var i = 0; i < bands.Count; i++)
            {
                var band = bands[i];
                var isLast = i == bands.Count - 1;
                var members = measured
                    .Where(item => item.NearestMeters >= band.Lower &&
                                   (isLast ? item.NearestMeters <= band.Upper : item.NearestMeters < band.Upper))
                    .Select(item => item.Feature)
                    .ToList();

                if (members.Count == 0)
                {
                    continue;
                }

                var color = BandColors[i % BandColors.Length];
                layers.Add(MarkersLayer(
                    operation.OperationId,
                    $"band-{i + 1}",
                    $"{sourceCategory.DisplayName} {FormatDistance(bands[i].Lower / toMeters)}–{FormatDistance(bands[i].Upper / toMeters)} {unit} from {targetCategory.DisplayName} in {area.DisplayName}",
                    $"{FormatDistance(bands[i].Lower / toMeters)}–{FormatDistance(bands[i].Upper / toMeters)} {unit}",
                    sourceCategory,
                    members,
                    color));
            }

            if (layers.Count == 0)
            {
                return SpatialSpecialistOutcome.Empty(coverage, warnings.Count > 0 ? warnings : null);
            }
        }
        else
        {
            var qualifying = measured.Where(item => item.NearestMeters <= limitMeters).ToList();
            if (qualifying.Count == 0)
            {
                return SpatialSpecialistOutcome.Empty(coverage, warnings.Count > 0 ? warnings : null);
            }

            layers.Add(MarkersLayer(
                operation.OperationId,
                $"within-{firstUpper:0.###}{unit}",
                $"{sourceCategory.DisplayName} within {FormatDistance(firstUpper)} {unit} of {targetCategory.DisplayName} in {area.DisplayName}",
                $"{sourceCategory.DisplayName} ≤ {FormatDistance(firstUpper)} {unit}",
                sourceCategory,
                qualifying.Select(item => item.Feature)));
        }

        // Buffers show the distance rule itself, so they belong to a within search only. A
        // "beyond" result qualifies the area OUTSIDE the ring; drawing the ring as if it were the
        // result would invert the meaning. Bands are already differentiated by colour.
        if (!isBanded && !isBeyond)
        {
            var buffered = targets.Take(MaxBufferLayers).ToList();
            foreach (var target in buffered)
            {
                layers.Add(new SpatialLayerDraft(
                    LayerId: $"{operation.OperationId}:buffer:{target.Id}",
                    SourceOperationId: operation.OperationId,
                    Name: $"{FormatDistance(firstUpper)} {unit} around {DisplayName(target)}",
                    LegendLabel: $"Buffer around {DisplayName(target)}",
                    Kind: SpatialLayerKind.Buffer,
                    Ring: GeometryMath.CirclePolygon(
                        new GeoPoint(target.Latitude, target.Longitude), limitMeters, segments: 32),
                    Style: new SpatialLayerStyle(targetCategory.Color, Opacity: 0.15, StrokeWidth: 2),
                    Attributes: new Dictionary<string, string> { ["category"] = targetCategory.Key }));
            }

            if (targets.Count > MaxBufferLayers)
            {
                warnings.Add($"Buffers are drawn for the first {MaxBufferLayers} {targetCategory.DisplayName.ToLowerInvariant()} only.");
            }
        }

        return new SpatialSpecialistOutcome(
            SpatialOperationStatus.Succeeded,
            layers,
            coverage,
            FailureReason: null,
            Warnings: warnings.Count > 0 ? warnings : null);
    }

    private static SpatialLayerDraft MarkersLayer(
        string operationId,
        string layerSuffix,
        string name,
        string legendLabel,
        PoiCategoryDefinition sourceCategory,
        IEnumerable<PoiFeature> features,
        string? color = null) =>
        new(
            LayerId: $"{operationId}:{layerSuffix}",
            SourceOperationId: operationId,
            Name: name,
            LegendLabel: legendLabel,
            Kind: SpatialLayerKind.Markers,
            Markers: features
                .OrderBy(feature => feature.Name, StringComparer.OrdinalIgnoreCase)
                .Select(feature => new SpatialMarker(feature.Id, feature.Name, feature.Latitude, feature.Longitude))
                .ToList(),
            Style: new SpatialLayerStyle(color ?? sourceCategory.Color, sourceCategory.MarkerShape),
            Attributes: new Dictionary<string, string> { ["category"] = sourceCategory.Key });

    private static FeatureSelector? FindSelector(IReadOnlyList<FeatureSelector> targets, string role) =>
        targets.FirstOrDefault(target =>
            string.Equals(target.Role, role, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(target.Category))
        ?? (role == "source"
            ? targets.FirstOrDefault(target => !string.IsNullOrWhiteSpace(target.Category))
            : null);

    private SpatialSpecialistOutcome Unsupported(string? phrase) =>
        SpatialSpecialistOutcome.Failed(
            $"I can't map \"{phrase}\" — I know these place types: {categoryCatalog.DescribeSupported()}.");

    private static string DisplayName(PoiFeature feature) =>
        string.IsNullOrWhiteSpace(feature.Name) ? "an unnamed place" : feature.Name;

    private static string FormatDistance(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
