using System.Text.Json;
using AskLucy.Domain.SiteAnalysis;
using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.SiteAnalysis.Queries.GetSiteAnalysis;

/// <summary>
/// contracts/site-analysis-api.md 200 OK shape. <see cref="SiteAnalysisResultDetailDto.Content"/>
/// is populated only for <see cref="SiteAnalysisResultStatus.Completed"/>. <b>FailureReason is
/// deliberately not exposed</b> — it is operator-only diagnostic detail (FR-022), consistent with
/// FR-024's per-specialist silence.
/// </summary>
public sealed record SiteAnalysisResultDetailDto(
    Guid Id,
    string AnalysisType,
    string Status,
    string? DataSource,
    string? ConfidenceLevel,
    Guid? DocumentId,
    JsonElement? Content,
    DateTime CompletedAtUtc)
{
    public static SiteAnalysisResultDetailDto Create(SiteAnalysisResult result) => new(
        result.Id,
        result.AnalysisType.ToString(),
        result.Status.ToString(),
        result.DataSource,
        result.ConfidenceLevel?.ToString(),
        result.DocumentId,
        result.ContentJson is null ? null : JsonSerializer.Deserialize<JsonElement>(result.ContentJson),
        result.CompletedAtUtc);
}

public sealed record SiteAnalysisDetailDto(
    Guid Id,
    Guid UserChatId,
    string SiteName,
    double Latitude,
    double Longitude,
    string Status,
    int ExpectedResultCount,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<SiteAnalysisResultDetailDto> Results)
{
    public static SiteAnalysisDetailDto Create(SiteAnalysisAggregate analysis) => new(
        analysis.Id,
        analysis.UserChatId,
        analysis.SiteName,
        analysis.Latitude,
        analysis.Longitude,
        analysis.Status.ToString(),
        analysis.ExpectedResultCount,
        analysis.StartedAtUtc,
        analysis.CompletedAtUtc,
        [.. analysis.Results.Select(SiteAnalysisResultDetailDto.Create)]);
}
