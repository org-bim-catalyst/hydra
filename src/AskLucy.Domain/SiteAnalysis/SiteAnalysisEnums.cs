namespace AskLucy.Domain.SiteAnalysis;

/// <summary>specs/057-site-analysis-agent data-model.md — the analysis as a whole. Both terminal
/// states set <see cref="SiteAnalysis.CompletedAtUtc"/>; <c>Failed</c> covers both "every
/// specialist failed" and "dispatch itself failed" (FR-021).</summary>
public enum SiteAnalysisStatus
{
    Running,
    Completed,
    Failed,
}

/// <summary>One specialist's outcome (FR-022). <c>Rejected</c> is distinct from <c>Failed</c>: the
/// specialist itself succeeded but the parent relay's validation gate refused the result
/// (contracts/result-relay.md).</summary>
public enum SiteAnalysisResultStatus
{
    Completed,
    Failed,
    Rejected,
}

/// <summary>Rule-assigned, ordered (FR-013, data-model.md "Confidence assignment rule"). Never
/// derived from <c>GeocodingCandidate.Importance</c> — that field is populated on incompatible
/// scales by different providers (research.md D10).</summary>
public enum SiteAnalysisConfidenceLevel
{
    Low,
    Medium,
    High,
}

/// <summary>The specialist that produced a result. The extension point for FR-031 — a further
/// specialist adds one member here, one <c>IAgentTool</c> class, and one provisioner branch entry,
/// with no other structural change. <see cref="SchematicImage"/> ships in SPEC-057;
/// <see cref="UrbanContext"/> is the first follow-up specialist (the surroundings of a site —
/// adjacent land use, nearby building density and typology, and notable nearby places).</summary>
public enum SiteAnalysisType
{
    SchematicImage,
    UrbanContext,
}
