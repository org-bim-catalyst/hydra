using System.Text.Json;
using AskLucy.Domain.SiteAnalysis;

namespace AskLucy.Application.SiteAnalysis;

/// <summary>
/// contracts/result-relay.md — the coordinating agent's intake. The <b>only</b> route by which a
/// specialist's finding can reach the user (FR-007): a specialist that pushes a panel or a chat
/// message itself is a contract violation. Called <b>inline</b>, as the last step of a
/// specialist's own <c>ExecuteAsync</c> — never via workflow node-completion events, which
/// <c>WorkflowExecutionOrchestrator.ExecuteParallelAsync</c> batches until every branch settles
/// (research.md D3, tasks.md rule 1).
/// </summary>
public interface ISiteAnalysisResultRelay
{
    /// <summary>
    /// Validates, persists, and — only if valid — delivers a specialist's finding (FR-005, FR-006).
    /// A result failing any check in contracts/result-relay.md's validation table is persisted as
    /// <see cref="SiteAnalysisResultStatus.Rejected"/> and never delivered.
    /// </summary>
    Task ReportSuccessAsync(
        Guid siteAnalysisId,
        SiteAnalysisType analysisType,
        SiteAnalysisResultMetadata metadata,
        JsonDocument content,
        Guid? documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a specialist's own failure (FR-022/FR-022a) — captured, persisted with
    /// <paramref name="cause"/>'s detail, and structure-logged. Delivers nothing to the user
    /// (FR-024): constitution &#167;2 VIII governs capture and diagnosability, not user disclosure
    /// (research.md D13).
    /// </summary>
    Task ReportFailureAsync(
        Guid siteAnalysisId,
        SiteAnalysisType analysisType,
        string failureReason,
        Exception? cause,
        CancellationToken cancellationToken = default);
}
