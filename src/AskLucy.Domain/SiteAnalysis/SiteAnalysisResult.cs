using AskLucy.Domain.Common;

namespace AskLucy.Domain.SiteAnalysis;

/// <summary>
/// One specialist's outcome within a <see cref="SiteAnalysis"/> (data-model.md "SiteAnalysisResult").
/// Persisted for successes, failures, <b>and</b> rejections — a rejected result is evidence
/// (FR-022). Reachable only through the owning <see cref="SiteAnalysis"/> aggregate, never its own
/// <c>DbSet</c> (constitution &#167;5).
/// </summary>
public sealed class SiteAnalysisResult : BaseEntity
{
    public Guid SiteAnalysisId { get; private set; }

    public SiteAnalysisType AnalysisType { get; private set; }

    public SiteAnalysisResultStatus Status { get; private set; }

    /// <summary>The panel content-block document. Populated only when <see cref="Status"/> is <see cref="SiteAnalysisResultStatus.Completed"/>.</summary>
    public string? ContentJson { get; private set; }

    /// <summary>e.g. <c>"openai:gpt-image-1"</c>, <c>"osm-overpass"</c>, <c>"ai-interpretation"</c> (FR-011).</summary>
    public string? DataSource { get; private set; }

    public SiteAnalysisConfidenceLevel? ConfidenceLevel { get; private set; }

    /// <summary>The persisted generated image, when this specialist produced one (FR-029).</summary>
    public Guid? DocumentId { get; private set; }

    /// <summary>
    /// Diagnostic detail — required when <see cref="Status"/> is <see cref="SiteAnalysisResultStatus.Failed"/>
    /// or <see cref="SiteAnalysisResultStatus.Rejected"/>. Operator-only (FR-022); deliberately
    /// never exposed through the retrieval API (contracts/site-analysis-api.md).
    /// </summary>
    public string? FailureReason { get; private set; }

    public DateTime CompletedAtUtc { get; private set; }

    private SiteAnalysisResult()
    {
        // Required by EF Core materialization.
    }

    public static SiteAnalysisResult Completed(
        Guid siteAnalysisId,
        SiteAnalysisType analysisType,
        string contentJson,
        string dataSource,
        SiteAnalysisConfidenceLevel confidenceLevel,
        Guid? documentId,
        string actor)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            throw new DomainRuleViolationException("A completed site analysis result must carry content.");
        }

        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new DomainRuleViolationException("A completed site analysis result must carry a data source.");
        }

        return new SiteAnalysisResult
        {
            Id = Guid.CreateVersion7(),
            SiteAnalysisId = siteAnalysisId,
            AnalysisType = analysisType,
            Status = SiteAnalysisResultStatus.Completed,
            ContentJson = contentJson,
            DataSource = dataSource,
            ConfidenceLevel = confidenceLevel,
            DocumentId = documentId,
            CompletedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    /// <summary>The specialist itself failed (e.g. the provider was unavailable).</summary>
    public static SiteAnalysisResult Failed(Guid siteAnalysisId, SiteAnalysisType analysisType, string failureReason, string actor) =>
        CreateUnsuccessful(siteAnalysisId, analysisType, SiteAnalysisResultStatus.Failed, failureReason, actor);

    /// <summary>The specialist reported a result, but the parent relay's validation gate refused it (contracts/result-relay.md).</summary>
    public static SiteAnalysisResult Rejected(Guid siteAnalysisId, SiteAnalysisType analysisType, string failureReason, string actor) =>
        CreateUnsuccessful(siteAnalysisId, analysisType, SiteAnalysisResultStatus.Rejected, failureReason, actor);

    private static SiteAnalysisResult CreateUnsuccessful(
        Guid siteAnalysisId, SiteAnalysisType analysisType, SiteAnalysisResultStatus status, string failureReason, string actor)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new DomainRuleViolationException("An unsuccessful site analysis result must carry a failure reason.");
        }

        return new SiteAnalysisResult
        {
            Id = Guid.CreateVersion7(),
            SiteAnalysisId = siteAnalysisId,
            AnalysisType = analysisType,
            Status = status,
            FailureReason = failureReason,
            CompletedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }
}
