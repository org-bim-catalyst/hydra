using AskLucy.Application.Spatial.Density;

namespace AskLucy.Application.Spatial.Specialists;

/// <summary>Calculates density only through registered providers; delivery remains the coordinator's responsibility.</summary>
public sealed class DensityAnalysisSpecialist(
    IDensityDatasetCatalog catalog,
    IEnumerable<IDensityAnalysisProvider> providers,
    DensityAnalysisAuditLogger auditLogger) : ISpatialSpecialist
{
    private readonly IReadOnlyList<IDensityAnalysisProvider> _providers = [.. providers];
    public string OperationKind => SpatialOperationKinds.DensityAnalysis;

    public async Task<SpatialSpecialistOutcome> ExecuteAsync(SpatialOperation operation,
        SpatialExecutionContext context, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        var parameters = operation.DensityParameters;
        if (parameters is null || !catalog.TryGet(parameters.DatasetId, out var dataset))
            return SpatialSpecialistOutcome.Failed("I couldn't identify a supported dataset for that density analysis.");
        if (context.StudyArea is not { } area)
            return SpatialSpecialistOutcome.Failed("I need a bounded area before I can calculate density.");

        var method = parameters.Method == DensityMethod.Auto
            ? dataset.SupportedMethods.FirstOrDefault()
            : parameters.Method;
        if (method == DensityMethod.Auto || !dataset.SupportedMethods.Contains(method))
            return SpatialSpecialistOutcome.Failed($"{dataset.DisplayName} does not support the requested density method.");

        var definition = new DensityAnalysisDefinition(operation.OperationId, dataset, area, parameters.Intent,
            method, parameters.SearchRadiusMeters, parameters.CellSizeMeters, parameters.Classification);
        var provider = _providers.FirstOrDefault(p => p.SupportedDatasetIds.Contains(dataset.Id, StringComparer.OrdinalIgnoreCase));
        if (provider is null)
            return SpatialSpecialistOutcome.Failed($"The {dataset.DisplayName} density source is not currently available.");

        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            var result = await provider.CalculateAsync(definition, context, linked.Token).ConfigureAwait(false);
            auditLogger.Log(definition, result.Cells.Count == 0 ? "Empty" : "Succeeded", DateTime.UtcNow - started,
                result.Cells.Count, context.UserId, result.Coverage);
            if (result.Cells.Count == 0) return SpatialSpecialistOutcome.Empty(result.Coverage, result.Warnings);

            var layer = new SpatialLayerDraft($"density-{operation.OperationId}", operation.OperationId,
                $"{dataset.DisplayName} density in {area.DisplayName}", $"{dataset.DisplayName} density ({method})",
                SpatialLayerKind.DensitySurface, Style: new SpatialLayerStyle(Color: "#E53935", Opacity: 0.65),
                DensityCells: result.Cells, DensityClassification: result.Classification,
                DensityParameters: parameters);
            return new SpatialSpecialistOutcome(SpatialOperationStatus.Succeeded, [layer], result.Coverage,
                Warnings: result.Warnings);
        }
        catch (OperationCanceledException)
        {
            auditLogger.Log(definition, "Cancelled", DateTime.UtcNow - started, 0, context.UserId, [], "Timed out or cancelled");
            return SpatialSpecialistOutcome.Failed("The density analysis did not finish in time. Please try a smaller area.");
        }
        catch (Exception ex)
        {
            auditLogger.Log(definition, "Failed", DateTime.UtcNow - started, 0, context.UserId, [], ex.Message);
            return SpatialSpecialistOutcome.Failed("The density analysis could not be completed. Please try again.");
        }
    }
}
