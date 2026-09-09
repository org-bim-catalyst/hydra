using AskLucy.Application.Abstractions;
using AskLucy.Domain.Agents;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Conversations.Runtime;

internal static partial class TurnRecorderLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Turn record for chat {UserChatId} skipped: the orchestrator system agent is not yet provisioned")]
    public static partial void OrchestratorNotProvisioned(ILogger logger, Guid userChatId);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Recording the turn for chat {UserChatId} failed; the turn's own outcome is unaffected")]
    public static partial void RecordingFailed(ILogger logger, Guid userChatId, Exception exception);
}

/// <summary>
/// One delegation's outcome as the turn record sees it — the common shape both a flow's
/// <see cref="Flows.FlowStepResult"/> and a delegated <see cref="SubAgentDelegationResult"/>
/// convert into, so <see cref="TurnRecorder"/> needs to know about neither directly.
/// </summary>
public sealed record TurnRecordedStep(string CapabilityKey, bool Attempted, bool Succeeded, string? ResultJson, string? Reason);

/// <summary>
/// Writes one <see cref="AgentExecution"/> per capability-invoking conversational turn (specs/045
/// FR-038, T041/T111) — the integration T041 was deferred for, unblocked now that Phase 8
/// provisions a real <c>lucy.orchestrator</c> agent this can attribute a turn to.
///
/// <para>
/// <b>Written once, after the fact.</b> <see cref="RecordAsync"/> is called from
/// <see cref="ConversationTurnOrchestrator"/> after the turn's real work has already completed,
/// built from the same <see cref="Flows.FlowStepResult"/>/<see cref="SubAgentDelegationResult"/>
/// lists the offer step's own <see cref="TurnOutcome"/> is already built from — never
/// incrementally from inside <see cref="Flows.FlowRunner"/> or <see cref="SubAgentDelegator"/>'s
/// own loops. A conversational turn finishes in seconds inside one request, unlike a background
/// <see cref="AgentExecution"/> that can be paused/resumed/cancelled over minutes and genuinely
/// needs live, incremental persistence; this turn's inspectability need is "retrievable after the
/// fact" (FR-038), which one write at the end already satisfies, at a fraction of the risk of
/// threading a live recorder through three separately-tested execution paths.
/// </para>
///
/// <para>
/// <b>Fast-path turns write nothing</b> (research.md D8) — the caller only invokes this once the
/// decide step has actually named work to do.
/// </para>
///
/// <para>
/// <b>Not yet recorded, and why.</b> "Discarded suggestions" (FR-038) would need
/// <see cref="ITurnDecider"/> and the offer step's own grounder to return their dropped
/// candidates as data rather than only logging them — a real change to two already well-tested,
/// stable contracts, deliberately left for its own pass rather than bundled into this one.
/// Estimated cost needs a <c>ModelPricing</c> lookup keyed to one <c>AIModel</c>, but a single
/// turn can span two different provider/model pairs (the user's own chat model, used for
/// narration, and the <c>TurnOrchestration</c>-capability model used for the decide/offer steps)
/// with no single natural model to price against — recording it is deferred rather than picking
/// one of the two arbitrarily. <see cref="AgentExecutionUsage"/>/<see cref="AgentExecutionCost"/>
/// are therefore left unset on every recorded execution.
/// </para>
/// </summary>
public sealed class TurnRecorder(
    IAgentRepository agentRepository,
    IAgentExecutionRepository executionRepository,
    IUnitOfWork unitOfWork,
    ILogger<TurnRecorder> logger)
{
    public const string OrchestratorSystemKey = "lucy.orchestrator";
    private const int MaxTextLength = 2000;

    public async Task RecordAsync(
        Guid userChatId,
        string? userId,
        string objective,
        string planJson,
        IReadOnlyList<TurnRecordedStep> steps,
        string outcomeText,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            // AgentExecution.RunByUserId is a required foreign key into the Users table; an
            // unauthenticated caller has nothing valid to record against, and should never reach
            // a capability-invoking turn in the first place.
            return;
        }

        try
        {
            var orchestratorAgent = await agentRepository.GetBySystemKeyAsync(OrchestratorSystemKey, cancellationToken);
            var version = orchestratorAgent?.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (orchestratorAgent is null || version is null)
            {
                // A fresh deployment whose provisioning hosted service hasn't completed yet (or
                // was deferred — already surfaced separately on /health/ready). The turn itself
                // already ran and answered the user; only its audit trail is missing this once.
                TurnRecorderLog.OrchestratorNotProvisioned(logger, userChatId);
                return;
            }

            var execution = AgentExecution.Create(
                orchestratorAgent.Id, version.Id, userId, Truncate(objective), isTestExecution: false,
                AgentConversationIntegrationMode.ExistingConversation, userChatId, actor: userId);
            executionRepository.Add(execution);
            execution.Start();
            execution.SetPlan(planJson);

            for (var i = 0; i < steps.Count; i++)
            {
                RecordStep(execution, i, steps[i]);
            }

            execution.Complete(Truncate(outcomeText), finalOutputJson: null);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // The turn's real, user-visible outcome was already decided and streamed by the time
            // this runs — a failure here must not retroactively fail (or even delay) it. Isolation,
            // not suppression (constitution §2.VIII): logged with its cause, same posture as every
            // other capability-adjacent failure in this runtime.
            TurnRecorderLog.RecordingFailed(logger, userChatId, ex);
        }
    }

    private void RecordStep(AgentExecution execution, int stepIndex, TurnRecordedStep recorded)
    {
        var step = execution.AddStep(
            stepIndex, Truncate($"Invoke {recorded.CapabilityKey}"), AgentExecutionStepType.ToolCall,
            dependsOnStepId: null, recorded.CapabilityKey, inputJson: null);

        if (!recorded.Attempted)
        {
            step.Skip(Truncate(recorded.Reason ?? "not attempted"));
            return;
        }

        step.Start();
        var toolCall = AgentToolCall.Create(step.Id, recorded.CapabilityKey, AgentToolRiskLevel.Low, "[]", "{}", wasApprovalRequired: false);
        executionRepository.AddToolCall(toolCall);

        if (recorded.Succeeded)
        {
            toolCall.Complete(recorded.ResultJson ?? "{}");
            step.Complete(recorded.ResultJson);
            return;
        }

        var reason = Truncate(recorded.Reason ?? recorded.ResultJson ?? "it did not succeed");
        toolCall.Fail(reason);
        var error = execution.RecordError(AgentExecutionErrorCategory.ToolFailure, reason, step.Id, retryCount: 0);
        step.Fail(error.Id);
    }

    private static string Truncate(string value) => value.Length <= MaxTextLength ? value : value[..MaxTextLength];
}
