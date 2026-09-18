using AskLucy.Domain.SiteAnalysis;

namespace AskLucy.Application.SiteAnalysis;

/// <summary>contracts/result-relay.md § Interface — provenance a specialist reports alongside its content (FR-011). <see cref="ConfidenceLevel"/> is rule-assigned per specialist (data-model.md "Confidence assignment rule"), never derived from <c>GeocodingCandidate.Importance</c> (research.md D10).</summary>
public sealed record SiteAnalysisResultMetadata(
    SiteAnalysisType AnalysisType,
    string SiteName,
    string DataSource,
    SiteAnalysisConfidenceLevel ConfidenceLevel,
    DateTime GeneratedAtUtc,
    IReadOnlyList<string> Notes);
