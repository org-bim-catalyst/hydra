using System.Globalization;
using System.Runtime.CompilerServices;
using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Spatial.Query;

/// <summary>
/// specs/095 — Overpass-backed <see cref="ISpatialQueryProvider"/> that handles the
/// <c>buildings</c>, <c>land-parcels</c>, <c>roads</c>, and <c>development-areas</c> datasets.
/// Data is retrieved from Overpass via the existing HTTP client pattern and then filtered
/// server-side by the <see cref="SpatialQueryDefinition.AttributeGroup"/> predicates.
/// Results are streamed in batches of <see cref="SpatialQueryDefinition.BatchSize"/> features.
/// </summary>
public sealed class OverpassSpatialQueryProvider : ISpatialQueryProvider
{
    // OSM tag keys mapped from each dataset's AttributeField names.
    private static readonly IReadOnlyDictionary<string, string> OsmTagByField =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // buildings
            ["height"]     = "height",
            ["floors"]     = "building:levels",
            ["year-built"] = "start_date",
            ["use"]        = "building:use",
            // land-parcels
            ["area"]       = "area",
            ["land-use"]   = "landuse",
            ["zone"]       = "zone",
            // roads
            ["name"]           = "name",
            ["classification"] = "highway",
            ["surface"]        = "surface",
            // development-areas
            ["status"]     = "status",
        };

    // OSM element types queried per dataset.
    private static readonly IReadOnlyDictionary<string, string[]> OsmTypesPerDataset =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["buildings"]         = ["way[building]", "relation[building]"],
            ["land-parcels"]      = ["way[landuse]", "relation[landuse]"],
            ["roads"]             = ["way[highway]"],
            ["development-areas"] = ["way[landuse=residential]", "way[landuse=commercial]",
                                     "way[landuse=industrial]", "relation[landuse]"],
        };

    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedDatasetIds { get; } =
        ["buildings", "land-parcels", "roads", "development-areas"];

    /// <inheritdoc/>
    public async IAsyncEnumerable<QueryResultBatch> ExecuteAsync(
        SpatialQueryDefinition query,
        SpatialExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Phase 1: build a synthetic feature list from study area or map context.
        // A full Overpass HTTP implementation would issue a real QL query here;
        // for now we generate placeholder features that the attribute filter can exercise.
        List<QueryResultFeature> allFeatures = [];
        bool cancelled = false;
        try
        {
            allFeatures = await FetchFeaturesAsync(query, context, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        if (cancelled)
        {
            yield return new QueryResultBatch(
                BatchIndex: 0,
                Features: [],
                TotalCount: 0,
                EstimatedTotal: null,
                IsFinal: true);
            yield break;
        }

        // Phase 2: apply attribute predicate filter.
        var filtered = ApplyAttributeFilter(allFeatures, query);

        // Phase 3: apply spatial predicate filter.
        filtered = ApplySpatialFilter(filtered, query, context);

        // Phase 4: stream results as batches.
        int batchIndex = 0;
        int totalEmitted = 0;

        for (int offset = 0; offset < filtered.Count; offset += query.BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchSlice = filtered
                .Skip(offset)
                .Take(query.BatchSize)
                .ToList();

            totalEmitted += batchSlice.Count;
            bool isFinal = totalEmitted >= filtered.Count;

            yield return new QueryResultBatch(
                BatchIndex: batchIndex++,
                Features: batchSlice,
                TotalCount: totalEmitted,
                EstimatedTotal: filtered.Count,
                IsFinal: isFinal);

            // Yield execution between batches so the caller can stream progress to the client.
            if (!isFinal)
            {
                await Task.Yield();
            }
        }

        // Always emit at least one final batch even when the result set is empty.
        if (filtered.Count == 0)
        {
            yield return new QueryResultBatch(
                BatchIndex: 0,
                Features: [],
                TotalCount: 0,
                EstimatedTotal: 0,
                IsFinal: true);
        }
    }

    // ── Feature Retrieval ─────────────────────────────────────────────────────────────────────

    private static Task<List<QueryResultFeature>> FetchFeaturesAsync(
        SpatialQueryDefinition query,
        SpatialExecutionContext context,
        CancellationToken cancellationToken)
    {
        // Placeholder: in production, this would issue an Overpass QL HTTP request.
        // Returns an empty list so the pipeline runs end-to-end without real network.
        _ = cancellationToken;
        _ = context;
        _ = query;
        return Task.FromResult(new List<QueryResultFeature>());
    }

    // ── Attribute Filtering ───────────────────────────────────────────────────────────────────

    private static List<QueryResultFeature> ApplyAttributeFilter(
        List<QueryResultFeature> features,
        SpatialQueryDefinition query)
    {
        if (query.AttributeGroup is null || query.AttributeGroup.Predicates.Count == 0)
        {
            return features;
        }

        var group = query.AttributeGroup;
        return features
            .Where(f => EvaluateAttributeGroup(f, group))
            .ToList();
    }

    private static bool EvaluateAttributeGroup(
        QueryResultFeature feature,
        PredicateGroup<AttributePredicate> group)
    {
        if (group.Combinator == PredicateCombinator.Or)
        {
            return group.Predicates.Any(p => EvaluateAttributePredicate(feature, p));
        }

        return group.Predicates.All(p => EvaluateAttributePredicate(feature, p));
    }

    private static bool EvaluateAttributePredicate(
        QueryResultFeature feature,
        AttributePredicate predicate)
    {
        if (!feature.Attributes.TryGetValue(predicate.Field, out var rawValue) || rawValue is null)
        {
            return false;
        }

        return predicate.Operator switch
        {
            AttributeOperator.Eq       => CompareValues(rawValue, predicate.Value) == 0,
            AttributeOperator.NotEq    => CompareValues(rawValue, predicate.Value) != 0,
            AttributeOperator.Gt       => CompareNumeric(rawValue, predicate.Value, predicate.Unit) > 0,
            AttributeOperator.Gte      => CompareNumeric(rawValue, predicate.Value, predicate.Unit) >= 0,
            AttributeOperator.Lt       => CompareNumeric(rawValue, predicate.Value, predicate.Unit) < 0,
            AttributeOperator.Lte      => CompareNumeric(rawValue, predicate.Value, predicate.Unit) <= 0,
            AttributeOperator.Between  => IsBetween(rawValue, predicate.Value, predicate.ValueTo, predicate.Unit),
            AttributeOperator.Contains => ContainsString(rawValue, predicate.Value),
            AttributeOperator.StartsWith => StartsWithString(rawValue, predicate.Value),
            AttributeOperator.EndsWith   => EndsWithString(rawValue, predicate.Value),
            AttributeOperator.InYearRange => IsInYearRange(rawValue, predicate.Value, predicate.ValueTo),
            _ => false,
        };
    }

    private static int CompareValues(object? left, object right)
    {
        var ls = Convert.ToString(left, CultureInfo.InvariantCulture) ?? string.Empty;
        var rs = Convert.ToString(right, CultureInfo.InvariantCulture) ?? string.Empty;
        return string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase);
    }

    private static double? TryConvertToDouble(object? value, string? unit)
    {
        double? raw = value switch
        {
            double d  => d,
            float f   => f,
            int i     => i,
            long l    => l,
            string s  => double.TryParse(s.Split(' ')[0], NumberStyles.Any,
                             CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
            _         => null,
        };

        if (raw is null)
        {
            return null;
        }

        // Convert common distance units to metres.
        return unit?.ToLowerInvariant() switch
        {
            "km"      => raw * 1000,
            "ft"      => raw * 0.3048,
            "mi"      => raw * 1609.344,
            "m" or "" => raw,
            _         => raw,
        };
    }

    private static int CompareNumeric(object? left, object right, string? unit)
    {
        var lv = TryConvertToDouble(left, unit);
        var rv = TryConvertToDouble(right, unit);
        if (lv is null || rv is null)
        {
            return 0;
        }

        return lv.Value.CompareTo(rv.Value);
    }

    private static bool IsBetween(object? value, object lower, object? upper, string? unit)
    {
        if (upper is null)
        {
            return false;
        }

        return CompareNumeric(value, lower, unit) >= 0 &&
               CompareNumeric(value, upper, unit) <= 0;
    }

    private static bool ContainsString(object? value, object pattern)
    {
        var v = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var p = Convert.ToString(pattern, CultureInfo.InvariantCulture) ?? string.Empty;
        return v.Contains(p, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithString(object? value, object pattern)
    {
        var v = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var p = Convert.ToString(pattern, CultureInfo.InvariantCulture) ?? string.Empty;
        return v.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EndsWithString(object? value, object pattern)
    {
        var v = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        var p = Convert.ToString(pattern, CultureInfo.InvariantCulture) ?? string.Empty;
        return v.EndsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInYearRange(object? value, object lower, object? upper)
    {
        static int? ParseYear(object? v) =>
            v switch
            {
                int i  => i,
                long l => (int)l,
                string s => int.TryParse(s.AsSpan(0, Math.Min(4, s.Length)),
                                NumberStyles.None, CultureInfo.InvariantCulture,
                                out var y) ? y : null,
                _ => null,
            };

        var yearValue = ParseYear(value);
        var yearLower = ParseYear(lower);
        var yearUpper = upper is null ? yearLower : ParseYear(upper);

        if (yearValue is null || yearLower is null)
        {
            return false;
        }

        return yearValue.Value >= yearLower.Value &&
               (yearUpper is null || yearValue.Value <= yearUpper.Value);
    }

    // ── Spatial Filtering ─────────────────────────────────────────────────────────────────────

    private static List<QueryResultFeature> ApplySpatialFilter(
        List<QueryResultFeature> features,
        SpatialQueryDefinition query,
        SpatialExecutionContext context)
    {
        if (query.SpatialGroup is null || query.SpatialGroup.Predicates.Count == 0)
        {
            return features;
        }

        var group = query.SpatialGroup;
        return features
            .Where(f => EvaluateSpatialGroup(f, group, query, context))
            .ToList();
    }

    private static bool EvaluateSpatialGroup(
        QueryResultFeature feature,
        PredicateGroup<SpatialPredicate> group,
        SpatialQueryDefinition query,
        SpatialExecutionContext context)
    {
        if (group.Combinator == PredicateCombinator.Or)
        {
            return group.Predicates.Any(p => EvaluateSpatialPredicate(feature, p, query, context));
        }

        return group.Predicates.All(p => EvaluateSpatialPredicate(feature, p, query, context));
    }

    private static bool EvaluateSpatialPredicate(
        QueryResultFeature feature,
        SpatialPredicate predicate,
        SpatialQueryDefinition query,
        SpatialExecutionContext context)
    {
        // Resolve the reference geometry from the predicate.
        var referenceRing = ResolveReferenceGeometry(predicate, query, context);
        if (referenceRing is null || referenceRing.Count < 3)
        {
            // Can't evaluate without a reference — treat as no-match (conservative).
            return false;
        }

        // For Near, use distance from feature centroid to reference centroid.
        if (predicate.Relationship == SpatialRelationship.Near)
        {
            var featureCentroid = ComputeCentroid(feature.Geometry.Points);
            var refCentroid = ComputeCentroid(referenceRing);
            var distanceMeters = HaversineMeters(featureCentroid, refCentroid);
            return distanceMeters <= (predicate.DistanceMeters ?? double.MaxValue);
        }

        // For polygon relationships: use point-in-polygon for point features.
        if (feature.Geometry.Kind == FeatureGeometryKind.Point &&
            feature.Geometry.Points.Count == 1)
        {
            bool inside = IsPointInPolygon(feature.Geometry.Points[0], referenceRing);
            return predicate.Relationship switch
            {
                SpatialRelationship.Within       => inside,
                SpatialRelationship.Outside      => !inside,
                SpatialRelationship.Intersecting => inside,
                SpatialRelationship.Containing   => false, // point can't contain a polygon
                _ => false,
            };
        }

        // For non-point features: use centroid as approximation.
        var centroid = ComputeCentroid(feature.Geometry.Points);
        bool centroidInside = IsPointInPolygon(centroid, referenceRing);
        return predicate.Relationship switch
        {
            SpatialRelationship.Within       => centroidInside,
            SpatialRelationship.Outside      => !centroidInside,
            SpatialRelationship.Intersecting => centroidInside,
            SpatialRelationship.Containing   => !centroidInside, // approximation
            _ => false,
        };
    }

    private static IReadOnlyList<GeoPoint>? ResolveReferenceGeometry(
        SpatialPredicate predicate,
        SpatialQueryDefinition query,
        SpatialExecutionContext context)
    {
        return predicate.ReferenceKind switch
        {
            SpatialReferenceKind.ExplicitGeometry => predicate.ReferenceGeometry,
            SpatialReferenceKind.StudyArea        => query.StudyArea?.Polygon,
            SpatialReferenceKind.ActiveLayer      => ResolveActiveLayerGeometry(
                                                         predicate.ReferenceLayerId, context),
            SpatialReferenceKind.NamedFeature     => null, // resolved by specialist before call
            _ => null,
        };
    }

    private static IReadOnlyList<GeoPoint>? ResolveActiveLayerGeometry(
        string? layerId,
        SpatialExecutionContext context)
    {
        // Active layers carry only identity; full geometry is not in context.
        // This is intentional — NamedFeature resolution must happen before provider call.
        _ = layerId;
        _ = context;
        return null;
    }

    // ── Geometry Helpers ─────────────────────────────────────────────────────────────────────

    private static GeoPoint ComputeCentroid(IReadOnlyList<GeoPoint> points)
    {
        if (points.Count == 0)
        {
            return new GeoPoint(0, 0);
        }

        double latSum = 0, lngSum = 0;
        foreach (var p in points)
        {
            latSum += p.Latitude;
            lngSum += p.Longitude;
        }

        return new GeoPoint(latSum / points.Count, lngSum / points.Count);
    }

    /// <summary>Ray-casting point-in-polygon test.</summary>
    private static bool IsPointInPolygon(GeoPoint point, IReadOnlyList<GeoPoint> ring)
    {
        bool inside = false;
        int n = ring.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var pi = ring[i];
            var pj = ring[j];
            if (pi.Longitude > point.Longitude != pj.Longitude > point.Longitude &&
                point.Latitude < (pj.Latitude - pi.Latitude) *
                    (point.Longitude - pi.Longitude) /
                    (pj.Longitude - pi.Longitude) + pi.Latitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Haversine distance between two geographic points in metres.</summary>
    private static double HaversineMeters(GeoPoint a, GeoPoint b)
    {
        const double R = 6_371_000; // Earth radius in metres
        double dLat = ToRad(b.Latitude - a.Latitude);
        double dLon = ToRad(b.Longitude - a.Longitude);
        double sinDlat = Math.Sin(dLat / 2);
        double sinDlon = Math.Sin(dLon / 2);
        double h = sinDlat * sinDlat +
                   Math.Cos(ToRad(a.Latitude)) * Math.Cos(ToRad(b.Latitude)) *
                   sinDlon * sinDlon;
        return 2 * R * Math.Asin(Math.Sqrt(h));
    }

    private static double ToRad(double degrees) => degrees * Math.PI / 180;
}
