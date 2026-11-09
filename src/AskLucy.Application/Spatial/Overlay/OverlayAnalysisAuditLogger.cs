using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Spatial;

/// <summary>Structured, append-only operational audit for each overlay specialist execution.</summary>
public sealed partial class OverlayAnalysisAuditLogger(ILogger<OverlayAnalysisAuditLogger> logger)
{
    public void Log(
        OverlayAnalysisDefinition definition,
        string status,
        TimeSpan duration,
        int candidates,
        int excluded,
        IReadOnlyList<DataSourceCoverage> coverage) =>
        Executed(logger, definition.OperationId,
            string.Join(',', definition.Inputs.Select(input => input.Dataset.Id)),
            string.Join(',', definition.Criteria.Select(criterion => criterion.CriterionId)),
            status, duration.TotalMilliseconds, candidates, excluded,
            string.Join(',', coverage.Select(source => $"{source.SourceId}:{source.CoverageStatus}")));

    public void LogFailure(string operationId, IReadOnlyList<string> datasetIds, string reason) =>
        Failed(logger, operationId, string.Join(',', datasetIds), reason);

    public void LogException(OverlayAnalysisDefinition definition, TimeSpan duration, Exception exception) =>
        ProviderFailed(logger, definition.OperationId,
            string.Join(',', definition.Inputs.Select(input => input.Dataset.Id)), duration.TotalMilliseconds, exception);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "OverlayAnalysisExecuted OperationId={OperationId} Datasets={Datasets} Criteria={Criteria} Status={Status} DurationMs={DurationMs} Candidates={Candidates} Excluded={Excluded} Coverage={Coverage}")]
    private static partial void Executed(ILogger logger, string operationId, string datasets, string criteria,
        string status, double durationMs, int candidates, int excluded, string coverage);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "OverlayAnalysisRejected OperationId={OperationId} Datasets={Datasets} Reason={Reason}")]
    private static partial void Failed(ILogger logger, string operationId, string datasets, string reason);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "OverlayAnalysisProviderFailed OperationId={OperationId} Datasets={Datasets} DurationMs={DurationMs}")]
    private static partial void ProviderFailed(ILogger logger, string operationId, string datasets,
        double durationMs, Exception exception);
}
