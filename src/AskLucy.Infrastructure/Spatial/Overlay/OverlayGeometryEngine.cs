using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;
using AskLucy.Domain.SiteBoundaries;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace AskLucy.Infrastructure.Spatial.Overlay;

/// <summary>Projects WGS84 features into a local metric frame for one bounded overlay request.</summary>
public sealed class OverlayGeometryEngine(GeoPoint origin)
{
    private const double EarthRadiusMeters = 6_371_008.8;
    private static readonly GeometryFactory Factory = new();
    private readonly double _cosLatitude = Math.Cos(origin.Latitude * Math.PI / 180d);

    public Geometry ToGeometry(QueryResultFeature feature)
    {
        var coordinates = feature.Geometry.Points.Select(ToCoordinate).ToArray();
        Geometry geometry = feature.Geometry.Kind switch
        {
            FeatureGeometryKind.Point when coordinates.Length == 1 => Factory.CreatePoint(coordinates[0]),
            FeatureGeometryKind.LineString when coordinates.Length >= 2 => Factory.CreateLineString(coordinates),
            FeatureGeometryKind.Polygon when coordinates.Length >= 3 => Factory.CreatePolygon(ToRing(coordinates)),
            _ => throw new InvalidOperationException($"Feature '{feature.FeatureId}' has an unsupported geometry shape."),
        };

        if (!geometry.IsValid)
        {
            throw new InvalidOperationException($"Feature '{feature.FeatureId}' contains invalid geometry.");
        }

        return geometry;
    }

    public double AreaSquareMeters(QueryResultFeature feature) => ToGeometry(feature).Area;

    public double DistanceMeters(QueryResultFeature left, QueryResultFeature right) =>
        ToGeometry(left).Distance(ToGeometry(right));

    public bool Relates(QueryResultFeature left, QueryResultFeature right, OverlaySpatialRelation relation)
    {
        var first = ToGeometry(left);
        var second = ToGeometry(right);
        return relation switch
        {
            OverlaySpatialRelation.Within => first.Within(second),
            OverlaySpatialRelation.Contains => first.Contains(second),
            _ => first.Intersects(second),
        };
    }

    public IReadOnlyList<QueryResultFeature> ExecuteStep(
        OverlayStep step,
        IReadOnlyDictionary<string, IReadOnlyList<QueryResultFeature>> sources)
    {
        var inputFeatures = step.InputRefs.SelectMany(reference => sources[reference]).ToList();
        if (step.Kind == OverlayOperationKind.SpatialIntersection)
        {
            var candidates = sources[step.InputRefs[0]];
            var references = step.InputRefs.Skip(1).SelectMany(reference => sources[reference]).ToList();
            var relation = step.Relationship ?? OverlaySpatialRelation.Intersects;
            return candidates.Where(candidate => references.Any(reference => Relates(candidate, reference, relation)))
                .Select(candidate => candidate with
                {
                    Overlay = new OverlayFeatureMetadata(candidate.Overlay?.Eligibility ?? new Dictionary<string, bool>(),
                        candidate.Overlay?.CriterionScores ?? new Dictionary<string, double>(), candidate.Overlay?.Score,
                        candidate.Overlay?.Rank,
                        candidate.Overlay?.Provenance.Concat(references.Where(reference => Relates(candidate, reference, relation))
                            .Select(reference => reference.FeatureId)).Distinct(StringComparer.Ordinal).ToList()
                        ?? [candidate.FeatureId]),
                }).ToList();
        }

        var geometries = step.InputRefs.Select(reference => Union(sources[reference].Select(ToGeometry))).ToList();
        Geometry result = step.Kind switch
        {
            OverlayOperationKind.Intersection => geometries.Skip(1).Aggregate(geometries[0], (current, next) => current.Intersection(next)),
            OverlayOperationKind.Union => Union(geometries),
            OverlayOperationKind.Difference when geometries.Count == 2 => geometries[0].Difference(geometries[1]),
            OverlayOperationKind.Difference => throw new InvalidOperationException("Difference requires exactly two input layers."),
            _ => throw new InvalidOperationException("The requested overlay operation is unsupported."),
        };

        if (!result.IsValid)
        {
            throw new InvalidOperationException($"The {step.Kind} result is not valid geometry.");
        }

        var attributes = inputFeatures.FirstOrDefault()?.Attributes ?? new Dictionary<string, object?>();
        var provenance = inputFeatures.SelectMany(feature => feature.Overlay?.Provenance ?? [feature.FeatureId])
            .Distinct(StringComparer.Ordinal).ToList();
        return Components(result).Select((geometry, index) => ToFeature(
            geometry, $"overlay-{step.StepId}-{index + 1}", $"{step.Kind} result", attributes, provenance)).ToList();
    }

    public double CriterionValue(
        QueryResultFeature candidate,
        OverlayCriterion criterion,
        IReadOnlyDictionary<string, IReadOnlyList<QueryResultFeature>> sources,
        string candidateRef)
    {
        if (criterion.Kind == OverlayCriterionKind.Area)
        {
            var area = AreaSquareMeters(candidate);
            return criterion.Unit?.ToLowerInvariant() switch
            {
                "km2" => area / 1_000_000,
                _ => area,
            };
        }

        var related = criterion.InputRef.Equals(candidateRef, StringComparison.OrdinalIgnoreCase)
            ? [candidate]
            : sources[criterion.InputRef].Where(reference => Relates(candidate, reference, OverlaySpatialRelation.Intersects)).ToList();

        if (criterion.Kind == OverlayCriterionKind.Distance)
        {
            var nearest = sources[criterion.InputRef]
                .Select(reference => DistanceMeters(candidate, reference))
                .DefaultIfEmpty(double.PositiveInfinity).Min();
            return criterion.Unit?.ToLowerInvariant() == "km" ? nearest / 1000 : nearest;
        }

        if (criterion.Kind == OverlayCriterionKind.Attribute)
        {
            var values = related.Select(reference => ReadNumeric(reference.Attributes, criterion.Field)).Where(value => value.HasValue)
                .Select(value => value!.Value).ToList();
            if (values.Count == 0)
            {
                throw new InvalidOperationException($"The {criterion.Field} value is unavailable for a candidate.");
            }
            return values.Average();
        }

        throw new InvalidOperationException($"The {criterion.CriterionId} criterion cannot be used for numeric scoring.");
    }

    public static double? ReadNumeric(IReadOnlyDictionary<string, object?> attributes, string? key)
    {
        if (key is null || !attributes.TryGetValue(key, out var value) || value is null) return null;
        if (value is double d && double.IsFinite(d)) return d;
        if (value is IConvertible convertible)
        {
            try
            {
                var parsed = convertible.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                return double.IsFinite(parsed) ? parsed : null;
            }
            catch (FormatException) { return null; }
            catch (InvalidCastException) { return null; }
        }
        return null;
    }

    public static bool EvaluateAttribute(QueryResultFeature feature, OverlayCriterion criterion)
    {
        if (criterion.Field is null || !feature.Attributes.TryGetValue(criterion.Field, out var actual)) return false;
        if (criterion.Number is { } target && ReadNumeric(feature.Attributes, criterion.Field) is { } number)
        {
            return criterion.Operator.ToLowerInvariant() switch
            {
                "greaterthan" or "gt" => number > target,
                "greaterthanorequal" or "gte" => number >= target,
                "lessthan" or "lt" => number < target,
                "lessthanorequal" or "lte" => number <= target,
                "equals" or "eq" => number == target,
                "atleast" => number >= target,
                "atmost" => number <= target,
                _ => false,
            };
        }

        var text = Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture);
        return criterion.Operator.ToLowerInvariant() switch
        {
            "equals" or "eq" => string.Equals(text, criterion.Text, StringComparison.OrdinalIgnoreCase),
            "contains" => text?.Contains(criterion.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true,
            _ => false,
        };
    }

    private QueryResultFeature ToFeature(Geometry geometry, string id, string name,
        IReadOnlyDictionary<string, object?> attributes, IReadOnlyList<string> provenance)
    {
        if (geometry is Polygon polygon && polygon.NumInteriorRings > 0)
        {
            throw new InvalidOperationException("An overlay result contains polygon holes that the current viewer cannot represent.");
        }

        var points = geometry.Coordinates.Select(ToGeoPoint).ToList();
        var kind = geometry switch
        {
            Point => FeatureGeometryKind.Point,
            Polygon => FeatureGeometryKind.Polygon,
            LineString => FeatureGeometryKind.LineString,
            _ => throw new InvalidOperationException("An overlay result has an unsupported geometry type."),
        };
        return new QueryResultFeature(id, name, new FeatureGeometry(kind, points), attributes,
            new OverlayFeatureMetadata(new Dictionary<string, bool>(), new Dictionary<string, double>(), null, null, provenance));
    }

    private static IEnumerable<Geometry> Components(Geometry geometry)
    {
        if (geometry.IsEmpty) yield break;
        if (geometry is GeometryCollection collection)
        {
            for (var index = 0; index < collection.NumGeometries; index++)
            {
                foreach (var component in Components(collection.GetGeometryN(index))) yield return component;
            }
            yield break;
        }

        yield return geometry;
    }

    private static Geometry Union(IEnumerable<Geometry> geometries)
    {
        var features = geometries.Where(geometry => !geometry.IsEmpty).ToList();
        return features.Count == 0 ? Factory.CreateGeometryCollection([]) : UnaryUnionOp.Union(features);
    }

    private static LinearRing ToRing(Coordinate[] coordinates)
    {
        var closed = coordinates.Length > 0 && coordinates[0].Equals2D(coordinates[^1])
            ? coordinates
            : [.. coordinates, coordinates[0]];
        return Factory.CreateLinearRing(closed);
    }

    private Coordinate ToCoordinate(GeoPoint point)
    {
        var latitude = point.Latitude * Math.PI / 180d;
        var longitudeDelta = (point.Longitude - origin.Longitude) * Math.PI / 180d;
        var latitudeDelta = (point.Latitude - origin.Latitude) * Math.PI / 180d;
        return new Coordinate(EarthRadiusMeters * _cosLatitude * longitudeDelta,
            EarthRadiusMeters * latitudeDelta);
    }

    private GeoPoint ToGeoPoint(Coordinate coordinate)
    {
        var latitude = origin.Latitude + coordinate.Y / EarthRadiusMeters * 180d / Math.PI;
        var longitude = origin.Longitude + coordinate.X / (EarthRadiusMeters * _cosLatitude) * 180d / Math.PI;
        return new GeoPoint(latitude, longitude);
    }
}
