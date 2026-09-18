using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.Workflows;
using Hangfire;

namespace AskLucy.Application.SiteAnalysis;

/// <summary>
/// research.md D12, contracts — looks up the system-owned "site-analysis" workflow by <see cref="Workflow.SystemKey"/> and enqueues a fan-out execution directly, bypassing the ownership-guarded <c>StartWorkflowExecutionCommand</c> (plan.md &#167;Complexity Tracking).
/// <para>
/// Schedules through <see cref="IBackgroundJobClient"/>, deliberately NOT through
/// <see cref="IWorkflowExecutionRunner"/>: the runner's constructor takes the whole
/// <c>WorkflowExecutionOrchestrator</c>, whose node executors take <c>AgentToolCatalog</c>, which
/// takes every <c>IAgentTool</c> — including <c>RequestSiteAnalysisCapability</c>, which takes this
/// dispatcher. Depending on the runner here closes a DI cycle that hung every chat turn and fails
/// host startup validation. Only the job's <i>execution</i> needs the orchestrator, and Hangfire
/// resolves the runner for that in its own scope, outside this graph — so enqueueing the exact
/// call <c>WorkflowExecutionRunner.EnqueueAsync</c> makes is behaviourally identical.
/// </para>
/// </summary>
public sealed class SiteAnalysisDispatcher(
    IWorkflowRepository workflowRepository,
    IWorkflowExecutionRepository workflowExecutionRepository,
    IBackgroundJobClient backgroundJobClient,
    IUnitOfWork unitOfWork) : ISiteAnalysisDispatcher
{
    public const string WorkflowSystemKey = "site-analysis";

    private const string Actor = "system:site-analysis-dispatcher";

    public async Task<Guid> DispatchAsync(
        Guid siteAnalysisId, string runByUserId, string siteName, string siteLocation, double latitude, double longitude,
        CancellationToken cancellationToken = default)
    {
        var workflow = await workflowRepository.GetBySystemKeyAsync(WorkflowSystemKey, cancellationToken)
            ?? throw new DomainRuleViolationException($"The system workflow '{WorkflowSystemKey}' has not been provisioned.");

        if (workflow.PublishedVersionNumber is not { } versionNumber)
        {
            throw new DomainRuleViolationException($"The system workflow '{WorkflowSystemKey}' has no published version.");
        }

        var version = workflow.Versions.FirstOrDefault(v => v.VersionNumber == versionNumber)
            ?? throw new DomainRuleViolationException($"The system workflow '{WorkflowSystemKey}' is missing its published version {versionNumber}.");

        var inputsJson = JsonSerializer.Serialize(new
        {
            siteAnalysisId = siteAnalysisId.ToString(),
            siteName,
            siteLocation,
            latitude,
            longitude,
        });

        var execution = WorkflowExecution.Create(
            workflow.Id, version.Id, runByUserId, WorkflowExecutionTriggerType.EventDriven,
            triggeringEventReferenceJson: null, inputsJson, Actor);

        workflowExecutionRepository.Add(execution);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        backgroundJobClient.Enqueue<IWorkflowExecutionRunner>(r => r.RunJobAsync(execution.Id, CancellationToken.None));

        return execution.Id;
    }
}
