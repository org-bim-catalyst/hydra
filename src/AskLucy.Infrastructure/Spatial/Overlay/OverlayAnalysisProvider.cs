using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;
using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Infrastructure.Spatial.Overlay;

/// <summary>Loads bounded catalog layers through existing query providers and executes overlay steps.</summary>
public sealed class OverlayAnalysisProvider(IEnumerable<ISpatialQueryProvider> queryProviders) : IOverlayAnalysisProvider
{
    private readonly IReadOnlyList<ISpatialQueryProvider> _queryProviders = [.. queryProviders];

    public async Task<OverlayAnalysisProviderResult> ExecuteAsync(
        OverlayAnalysisDefinition definition,
        SpatialExecutionContext context,
        CancellationToken cancellationToken)
    {
        var coverage = new List<DataSourceCoverage>();
        var warnings = new List<string>();
        if (definition.StudyArea.RadiusMeters <= 0 || !double.IsFinite(definition.StudyArea.RadiusMeters))
        {
            return new OverlayAnalysisProviderResult([], 0, 0, coverage, warnings,
                "Overlay analysis needs a valid bounded study area.");
        }

        var geometry = new OverlayGeometryEngine(definition.StudyArea.Center);
        var data = new Dictionary<string, IReadOnlyList<QueryResultFeature>>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in definition.Inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = _queryProviders.FirstOrDefault(candidate => candidate.SupportedDatasetIds
                .Contains(input.Dataset.Id, StringComparer.OrdinalIgnoreCase));
            if (provider is null)
            {
                return Failed($"The {input.Dataset.DisplayName} source is not available for overlay analysis.");
            }

            var query = new SpatialQueryDefinition(
                definition.OperationId,
                input.Dataset,
                AttributeGroup: null,
                SpatialGroup: null,
                StudyArea: definition.StudyArea,
                BatchSize: 500);
            var features = new List<QueryResultFeature>();
            var isFinal = false;
            try
            {
                await foreach (var batch in provider.ExecuteAsync(query, context, cancellationToken).ConfigureAwait(false))
                {
                    features.AddRange(batch.Features);
                    if (batch.IsFinal)
                    {
                        isFinal = true;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return Failed($"The {input.Dataset.DisplayName} source failed: {exception.GetType().Name}.");
            }

            if (!isFinal)
            {
                return Failed($"The {input.Dataset.DisplayName} source did not complete its result stream.");
            }

            var invalidFeatures = features.Where(feature => !IsValidFeature(feature, geometry)).ToList();
            if (invalidFeatures.Count > 0)
            {
                features = features.Except(invalidFeatures).ToList();
                coverage.Add(DataSourceCoverage.Partial(input.Dataset.Id, input.Dataset.DisplayName,
                    $"Excluded {invalidFeatures.Count:N0} invalid geometries."));
                warnings.Add($"{invalidFeatures.Count:N0} invalid {input.Dataset.DisplayName} geometries were excluded.");
            }
            else
            {
                coverage.Add(DataSourceCoverage.Complete(input.Dataset.Id, input.Dataset.DisplayName));
            }

            var outsideArea = features.Where(feature => !IsInsideArea(feature, definition.StudyArea, geometry)).ToList();
            features = features.Except(outsideArea).ToList();

            data[input.Input.Ref] = features;
        }

        foreach (var step in definition.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            data[step.OutputRef] = geometry.ExecuteStep(step, data);
        }

        var candidateRef = definition.Steps.Count > 0
            ? definition.Steps[^1].OutputRef
            : definition.CandidateRef;
        if (!data.TryGetValue(candidateRef, out var rawCandidates))
        {
            return Failed("The overlay candidate output could not be resolved.");
        }

        var candidates = rawCandidates.ToList();
        foreach (var criterion in definition.Criteria.Where(criterion => criterion.Required))
        {
            var before = candidates.Count;
            candidates = candidates.Where(candidate => EvaluateCriterion(candidate, criterion, data,
                definition.CandidateRef, geometry)).ToList();
            if (before > candidates.Count)
            {
                warnings.Add($"{before - candidates.Count:N0} candidates did not meet {criterion.CriterionId}.");
            }
        }

        if (definition.Scoring is not null)
        {
            foreach (var criterion in definition.Criteria.Where(criterion => definition.Scoring.Weights.Any(weight =>
                         string.Equals(weight.CriterionId, criterion.CriterionId, StringComparison.OrdinalIgnoreCase))))
            {
                candidates = candidates.Select(candidate =>
                {
                    var value = geometry.CriterionValue(candidate, criterion, data, definition.CandidateRef);
                    var attributes = new Dictionary<string, object?>(candidate.Attributes, StringComparer.OrdinalIgnoreCase)
                    {
                        [criterion.CriterionId] = value,
                    };
                    return candidate with { Attributes = attributes };
                }).ToList();
            }
        }

        candidates = candidates.Select(candidate => candidate with
        {
            Overlay = new OverlayFeatureMetadata(
                definition.Criteria.Where(criterion => criterion.Required)
                    .ToDictionary(criterion => criterion.CriterionId, _ => true, StringComparer.OrdinalIgnoreCase),
                candidate.Overlay?.CriterionScores ?? new Dictionary<string, double>(), null, null,
                candidate.Overlay?.Provenance ?? [candidate.FeatureId]),
        }).ToList();

        var layers = new List<OverlayOutputLayer>();
        foreach (var input in definition.Inputs)
        {
            layers.Add(new OverlayOutputLayer(input.Input.Ref,
                $"{input.Dataset.DisplayName} · input", "input", "Source dataset", data[input.Input.Ref]));
        }

        var lastStep = definition.Steps[^1];
        layers.Add(new OverlayOutputLayer(lastStep.OutputRef,
            $"Overlay result · {lastStep.Kind}", "result", lastStep.Kind.ToString(), candidates));

        return new OverlayAnalysisProviderResult(layers, rawCandidates.Count,
            Math.Max(0, rawCandidates.Count - candidates.Count), coverage, warnings);

        OverlayAnalysisProviderResult Failed(string reason) => new([], 0, 0, coverage, warnings, reason);
    }

    private static bool EvaluateCriterion(
        QueryResultFeature candidate,
        OverlayCriterion criterion,
        IReadOnlyDictionary<string, IReadOnlyList<QueryResultFeature>> data,
        string candidateRef,
        OverlayGeometryEngine geometry)
    {
        switch (criterion.Kind)
        {
            case OverlayCriterionKind.Attribute:
                {
                    var matches = criterion.InputRef.Equals(candidateRef, StringComparison.OrdinalIgnoreCase)
                        ? [candidate]
                        : data[criterion.InputRef].Where(reference => geometry.Relates(candidate, reference, OverlaySpatialRelation.Intersects)).ToList();
                    return matches.Any(feature => OverlayGeometryEngine.EvaluateAttribute(feature, criterion));
                }
            case OverlayCriterionKind.Area:
                {
                    var area = geometry.AreaSquareMeters(candidate);
                    var threshold = criterion.Unit?.ToLowerInvariant() == "km2" ? criterion.Number!.Value * 1_000_000 : criterion.Number!.Value;
                    return Compare(area, threshold, criterion.Operator);
                }
            case OverlayCriterionKind.Distance:
                {
                    var distance = data[criterion.InputRef].Select(reference => geometry.DistanceMeters(candidate, reference))
                        .DefaultIfEmpty(double.PositiveInfinity).Min();
                    var threshold = criterion.Unit?.ToLowerInvariant() == "km" ? criterion.Number!.Value * 1000 : criterion.Number!.Value;
                    return Compare(distance, threshold, criterion.Operator);
                }
            case OverlayCriterionKind.SpatialRelationship:
                {
                    var relation = criterion.Operator.ToLowerInvariant() switch
                    {
                        "within" => OverlaySpatialRelation.Within,
                        "contains" => OverlaySpatialRelation.Contains,
                        _ => OverlaySpatialRelation.Intersects,
                    };
                    return data[criterion.InputRef].Any(reference => geometry.Relates(candidate, reference, relation));
                }
            default:
                return false;
        }
    }

    private static bool Compare(double value, double threshold, string op) => op.ToLowerInvariant() switch
    {
        "within" or "lessthan" or "lt" or "atmost" or "lte" => value <= threshold,
        "atleast" or "greaterthanorequal" or "gte" => value >= threshold,
        "greaterthan" or "gt" => value > threshold,
        "equals" or "eq" => Math.Abs(value - threshold) < 1e-9,
        _ => false,
    };

    private static bool IsInsideArea(QueryResultFeature feature, ResolvedStudyArea area, OverlayGeometryEngine geometry)
    {
        if (area.Polygon is { Count: >= 3 } polygon)
        {
            var boundary = new QueryResultFeature("study-area", area.DisplayName,
                new FeatureGeometry(FeatureGeometryKind.Polygon, polygon), new Dictionary<string, object?>());
            return geometry.Relates(feature, boundary, OverlaySpatialRelation.Intersects);
        }

        if (area.Viewport is { } viewport)
        {
            return feature.Geometry.Points.All(point => point.Latitude >= viewport.SouthwestLat &&
                point.Latitude <= viewport.NortheastLat && point.Longitude >= viewport.SouthwestLng &&
                point.Longitude <= viewport.NortheastLng);
        }

        return true;
    }

    private static bool IsValidFeature(QueryResultFeature feature, OverlayGeometryEngine geometry)
    {
        try
        {
            _ = geometry.ToGeometry(feature);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
