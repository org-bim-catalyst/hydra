using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Spatial.Density;

/// <summary>Append-only structured audit event for every executed density operation.</summary>
public sealed class DensityAnalysisAuditLogger(ILogger<DensityAnalysisAuditLogger> logger)
{
    public void Log(DensityAnalysisDefinition definition, string status, TimeSpan duration,
        int cellCount, string? userId, IReadOnlyList<DataSourceCoverage> coverage, string? reason = null) =>
        logger.LogInformation(new EventId(9510, "DensityAnalysisExecuted"),
            "DensityAnalysisExecuted AnalysisId={AnalysisId} DatasetId={DatasetId} Method={Method} " +
            "RadiusMeters={RadiusMeters} CellSizeMeters={CellSizeMeters} Status={Status} DurationMs={DurationMs} " +
            "CellCount={CellCount} Coverage={Coverage} UserId={UserId} FailureReason={FailureReason}",
            definition.AnalysisId, definition.Dataset.Id, definition.Method, definition.SearchRadiusMeters,
            definition.CellSizeMeters, status, (long)duration.TotalMilliseconds, cellCount,
            string.Join(",", coverage.Select(x => x.CoverageStatus)), userId, reason);
}
