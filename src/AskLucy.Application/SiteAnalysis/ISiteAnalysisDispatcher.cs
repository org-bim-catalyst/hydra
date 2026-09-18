namespace AskLucy.Application.SiteAnalysis;

/// <summary>research.md D12 — dispatches the fan-out workflow for an already-created <see cref="Domain.SiteAnalysis.SiteAnalysis"/> record. Deliberately bypasses <c>StartWorkflowExecutionCommand</c>, whose <c>WorkflowOwnershipGuard</c> hard-checks <c>OwnerId == userId</c> and can never be satisfied by the shared, system-owned "site-analysis" workflow.</summary>
public interface ISiteAnalysisDispatcher
{
    /// <summary>Enqueues the fan-out and returns the workflow execution id. <paramref name="runByUserId"/> MUST be the real signed-in user — both <see cref="Abstractions.IPanelNotifier"/> and <see cref="Abstractions.ISiteAnalysisNotifier"/> key their pushes by user id (tasks.md rule 3).</summary>
    Task<Guid> DispatchAsync(
        Guid siteAnalysisId, string runByUserId, string siteName, string siteLocation, double latitude, double longitude,
        CancellationToken cancellationToken = default);
}
