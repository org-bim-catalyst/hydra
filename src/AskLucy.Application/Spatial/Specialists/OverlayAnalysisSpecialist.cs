using AskLucy.Application.Spatial.Query;

namespace AskLucy.Application.Spatial.Specialists;

/// <summary>Coordinates one overlay operation; data retrieval/execution and delivery remain behind shared seams.</summary>
public sealed class OverlayAnalysisSpecialist(
    IDatasetCatalog datasetCatalog,
    IOverlayAnalysisProvider provider,
    OverlayAnalysisAuditLogger auditLogger) : ISpatialSpecialist
{
    public string OperationKind => SpatialOperationKinds.OverlayAnalysis;

    public async Task<SpatialSpecialistOutcome> ExecuteAsync(
        SpatialOperation operation,
        SpatialExecutionContext context,
        CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var parameters = operation.OverlayParameters;
        if (parameters is null)
        {
            return Fail("I need the datasets and criteria before I can run that overlay.");
        }

        if (context.StudyArea is not { RadiusMeters: > 0 } area)
        {
            return Fail("I need a bounded study area before I can run overlay analysis.");
        }

        var resolved = new List<OverlayResolvedInput>();
        foreach (var input in parameters.Inputs)
        {
            if (!datasetCatalog.TryGet(input.DatasetId, out var dataset))
            {
                return Fail($"The {input.DatasetId} dataset is not available for overlay analysis.");
            }

            resolved.Add(new OverlayResolvedInput(input, dataset));
        }

        foreach (var criterion in parameters.Criteria.Where(criterion => criterion.Kind == OverlayCriterionKind.Attribute))
        {
            var fieldAvailable = resolved.Where(candidate =>
                    string.Equals(candidate.Input.Ref, criterion.InputRef, StringComparison.OrdinalIgnoreCase) ||
                    parameters.Steps.Any(step => string.Equals(step.OutputRef, criterion.InputRef, StringComparison.OrdinalIgnoreCase) &&
                        step.InputRefs.Contains(candidate.Input.Ref, StringComparer.OrdinalIgnoreCase)))
                .Any(candidate => candidate.Dataset.AttributeFields.Any(field =>
                    string.Equals(field.Name, criterion.Field, StringComparison.OrdinalIgnoreCase)));
            if (!fieldAvailable)
            {
                return Fail($"The {criterion.Field} field is not available in the selected overlay inputs.");
            }
        }

        var definition = new OverlayAnalysisDefinition(
            operation.OperationId,
            area.DisplayName,
            area,
            parameters.CandidateRef,
            resolved,
            parameters.Steps,
            parameters.Criteria,
            parameters.Scoring);

        try
        {
            var result = await provider.ExecuteAsync(definition, context, cancellationToken).ConfigureAwait(false);
            var resultFeatures = result.Layers.Where(layer => layer.Role == "result")
                .SelectMany(layer => layer.Features).ToList();
            auditLogger.Log(definition, result.FailureReason is not null ? "Failed" :
                resultFeatures.Count == 0 ? "Empty" : "Succeeded", DateTime.UtcNow - started,
                result.CandidateCount, result.ExcludedCount, result.Coverage);

            if (result.FailureReason is { Length: > 0 } failure)
            {
                return SpatialSpecialistOutcome.Failed(failure, result.Warnings);
            }

            if (parameters.Scoring is not null)
            {
                var scored = OverlayScorer.Score(resultFeatures, parameters.Criteria, parameters.Scoring);
                var byId = scored.ToDictionary(feature => feature.FeatureId, StringComparer.Ordinal);
                result = result with
                {
                    Layers = result.Layers.Select(layer => layer.Role == "result"
                        ? layer with { Features = layer.Features.Select(feature => byId.GetValueOrDefault(feature.FeatureId, feature)).ToList() }
                        : layer).ToList(),
                };
            }

            var layers = result.Layers.Select(layer => new SpatialLayerDraft(
                LayerId: $"overlay-{operation.OperationId}-{layer.Ref}",
                SourceOperationId: operation.OperationId,
                Name: layer.Name,
                LegendLabel: $"{layer.Name} ({layer.Features.Count:N0})",
                Kind: SpatialLayerKind.FeatureQuery,
                Features: layer.Features,
                Style: new SpatialLayerStyle(Color: layer.Role == "result" ? "#00897B" : "#5C6BC0", Opacity: 0.28, StrokeWidth: 2),
                Overlay: new OverlayLayerMetadata(
                    layer.Role,
                    layer.Operation,
                    parameters.Criteria.Select(criterion => criterion.CriterionId).ToList(),
                    parameters.Inputs.Select(input => input.DatasetId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    BuildScoreMeaning(parameters),
                    result.Coverage.Select(source => $"{source.DisplayName}: {source.CoverageStatus}").ToList()),
                Coverage: result.Coverage))
                .ToList();

            if (layers.Count == 0)
            {
                return SpatialSpecialistOutcome.Empty(result.Coverage, result.Warnings);
            }

            return new SpatialSpecialistOutcome(SpatialOperationStatus.Succeeded, layers, result.Coverage,
                Warnings: result.Warnings.Count > 0 ? result.Warnings : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            auditLogger.Log(definition, "Cancelled", DateTime.UtcNow - started, 0, 0, []);
            throw;
        }
        catch (Exception exception)
        {
            auditLogger.LogException(definition, DateTime.UtcNow - started, exception);
            return SpatialSpecialistOutcome.Failed("Overlay analysis could not be completed. Please try a smaller area or different datasets.",
                [$"The overlay provider failed. Reference operation {operation.OperationId} for support."]);
        }

        SpatialSpecialistOutcome Fail(string reason)
        {
            auditLogger.LogFailure(operation.OperationId, parameters?.Inputs.Select(input => input.DatasetId).ToArray() ?? [], reason);
            return SpatialSpecialistOutcome.Failed(reason);
        }
    }

    private static string? BuildScoreMeaning(OverlayAnalysisParameters parameters)
    {
        if (parameters.Scoring is not { Weights.Count: > 0 } scoring)
        {
            return null;
        }

        var byId = parameters.Criteria.ToDictionary(criterion => criterion.CriterionId, StringComparer.OrdinalIgnoreCase);
        var total = scoring.Weights.Sum(weight => weight.Weight);
        var weights = scoring.Weights.Select(weight =>
        {
            var criterion = byId[weight.CriterionId];
            var direction = criterion.Direction == OverlayCriterionDirection.LowerIsBetter
                ? "lower values rank higher"
                : "higher values rank higher";
            return $"{criterion.CriterionId} {weight.Weight / total:P0} ({direction})";
        });

        return $"Weighted min-max score (0–1); {string.Join(", ", weights)}. Ties use feature ID.";
    }
}
