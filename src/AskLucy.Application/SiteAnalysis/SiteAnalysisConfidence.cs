using AskLucy.Domain.SiteAnalysis;

namespace AskLucy.Application.SiteAnalysis;

/// <summary>
/// data-model.md § "Confidence assignment rule" (FR-013) — the single place every specialist
/// computes its <see cref="SiteAnalysisConfidenceLevel"/>, so the rule is applied consistently
/// rather than each specialist inventing its own judgement call.
///
/// <para>
/// <b>Never derive confidence from <c>GeocodingCandidate.Importance</c></b> (research.md D10,
/// tasks.md rule 6): Google's and Nominatim's <c>Importance</c> values are populated on
/// incompatible scales — Google uses a fixed lookup by <c>location_type</c>, Nominatim passes
/// through the raw OSM importance float — and conflating them already caused a production defect
/// in this codebase. <see cref="ResolveConfidence"/> takes only plain grounding facts as
/// parameters; it has no way to accept an <c>Importance</c>-shaped value even by accident.
/// </para>
/// </summary>
public static class SiteAnalysisConfidence
{
    /// <summary>
    /// <paramref name="dataDirectlyDescribesSite"/> — the data specifically and directly describes
    /// the analyzed site (e.g. an on-parcel OSM tag), not a nearby or generic value.
    /// <paramref name="dataIsSparseOrApproximate"/> — the data required interpolation, was
    /// nearby-only, or otherwise approximate rather than exact.
    /// When neither holds, the result is necessarily general-knowledge/model inference (Low).
    /// </summary>
    public static SiteAnalysisConfidenceLevel ResolveConfidence(bool dataDirectlyDescribesSite, bool dataIsSparseOrApproximate)
    {
        if (dataDirectlyDescribesSite)
        {
            return SiteAnalysisConfidenceLevel.High;
        }

        return dataIsSparseOrApproximate ? SiteAnalysisConfidenceLevel.Medium : SiteAnalysisConfidenceLevel.Low;
    }
}
