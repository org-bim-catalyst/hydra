using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Executes a <see cref="WorkflowExecution"/>'s graph node-by-node (spec.md FR-044-FR-048,
/// FR-029-FR-037, FR-039-FR-043, User Stories 1, 4, 5, 6, and 7) — pure orchestration logic, callable directly in tests
/// without Hangfire (<see cref="IWorkflowExecutionRunner"/> is the Hangfire-facing entry point,
/// mirroring <c>AgentExecutionOrchestrator</c>'s identical split).
///
/// <para>Runs inside a background job with no HTTP context, so it never depends on
/// <c>ICurrentUserAccessor</c> — every actor is <see cref="WorkflowExecution.RunByUserId"/>.</para>
///
/// <para>Every failure path — a thrown exception from node resolution, node execution, or a
/// persistence conflict — is caught once at the top level, recorded as a <see cref="WorkflowError"/>
/// row, and transitions the execution to <see cref="WorkflowExecutionStatus.Failed"/>; nothing is
/// ever silently swallowed (constitution §2.VIII).</para>
///
/// <para>The graph walk starts at the Start node and follows exactly one outgoing connection at a
/// time, except: a <see cref="WorkflowNodeType.Condition"/> node routes to exactly one of its two
/// <c>"true"</c>/<c>"false"</c>-labeled branches (FR-029) — every node reachable only through the
/// unchosen branch is recorded <see cref="WorkflowExecutionNodeStatus.Skipped"/>; a
/// <see cref="WorkflowNodeType.Parallel"/> node fans its branches out concurrently, gated by
/// <see cref="WorkflowBudgetGuard.ResolveMaxParallelNodes"/>, converging at a single
/// <see cref="WorkflowNodeType.Merge"/> node (FR-030/FR-031) — <b>only single-node branches are
/// supported</b> (a branch with more than one node before its Merge fails clearly rather than
/// risking concurrent, non-thread-safe access to the shared <see cref="WorkflowExecution"/>
/// aggregate/<see cref="IUnitOfWork"/>); a connection labeled <see cref="WorkflowConnection.LoopBackBranchLabel"/>
/// re-enters the loop body up to its budget-checked maximum iteration count (FR-032), then falls
/// through to whatever other outgoing connection the loop body's last node declares.</para>
///
/// <para><b>Human approval (FR-033-FR-037, research.md Decision 5)</b>: a <see
/// cref="WorkflowNodeType.HumanApproval"/> node always pauses; any other node whose resolved
/// underlying <see cref="IAgentTool"/> (for the six thin-adapter capability node types, via
/// <see cref="TryResolveUnderlyingToolName"/>) reports <see cref="AgentToolRiskLevel.High"/>/
/// <see cref="AgentToolRiskLevel.Critical"/> pauses too — this platform-mandatory baseline is
/// exactly what a matching, enabled <see cref="WorkflowPolicy"/> can auto-approve; a node's own,
/// author-configured <see cref="WorkflowNodeApprovalPolicy"/> can only ever make approval
/// <i>more</i> strict (force a pause a policy can never bypass), never less. <see cref="RunAsync"/>
/// is resumable: it re-derives every piece of walk state (resolved values, skipped nodes, loop
/// iteration counts) from the execution's own persisted <see cref="WorkflowExecutionNode"/> rows
/// every time it runs, rather than tracking a separate "resume point" flag, so a first run and a
/// resumed run share one code path (mirrors <c>AgentExecutionOrchestrator</c>'s identical
/// resumability strategy). <b>Timeout enforcement</b> (FR-037) is checked whenever <see
/// cref="RunAsync"/> is invoked while a decision is still pending — this needs some external,
/// periodic trigger to re-invoke it after the configured timeout actually elapses (e.g. a future
/// scheduled job); no such recurring trigger exists yet in this codebase, so an approval with no
/// further activity on its execution will not time out purely on its own until User Story 6 (or a
/// later pass) adds one — a known, documented limitation, not a silent gap.</para>
///
/// <para><b>Real-time monitoring (FR-048/FR-049, User Story 6)</b>: every persisted
/// <see cref="WorkflowExecutionEvent"/> row this orchestrator records — <c>WorkflowStarted</c>,
/// <c>NodeStarted</c>/<c>NodeCompleted</c>/<c>NodeFailed</c>, <c>ApprovalRequested</c>,
/// <c>WorkflowCompleted</c>/<c>WorkflowFailed</c> — is followed by a live push over
/// <see cref="IWorkflowExecutionNotifier"/> (never the other way around, so a client's payload
/// always mirrors an already-committed row). <c>ApprovalGranted</c>/<c>ApprovalRejected</c>/
/// <c>WorkflowPaused</c>/<c>WorkflowResumed</c>/<c>WorkflowCancelled</c> are pushed from their
/// respective command handlers instead, not here. Two interface members currently have no call
/// site at all: <c>NotifyNodeRetryingAsync</c> (there is no retry logic yet — User Story 7) and
/// <c>NotifyUsageUpdatedAsync</c> (workflow-level usage/cost aggregation isn't wired yet either) —
/// both exist to satisfy the full FR-049 event contract ahead of the stories that will actually
/// trigger them. The node-boundary loop also observes an externally-requested pause/cancel via
/// <see cref="IWorkflowExecutionRepository.GetStatusAsync"/> (SC-007: ≤5s), mirroring
/// <c>AgentExecutionOrchestrator</c>'s identical step-boundary check.</para>
///
/// <para><b>Retry, timeout, idempotency, and failure strategy (FR-039-FR-043, User Story 7)</b>: a
/// node's own <see cref="WorkflowNode.RetryPolicyJson"/> (parsed by <see cref="WorkflowNodeRetryPolicyParser"/>)
/// governs per-attempt backoff up to its configured maximum attempts, mirroring
/// <c>AgentExecutionOrchestrator.ExecuteToolWithRetryAsync</c>'s shape but configurable per node
/// rather than a fixed constant. Every attempt runs under a per-node timeout (<see
/// cref="WorkflowNode.TimeoutSeconds"/> or <see cref="Options.WorkflowRuntimeOptions.DefaultNodeTimeoutSeconds"/>)
/// via a linked <see cref="CancellationTokenSource"/>; an elapsed attempt is recorded as <see
/// cref="WorkflowErrorCategory.Timeout"/> and is itself subject to the same retry policy. A node
/// whose underlying capability requires a mutating <c>AgentToolPermission</c> and declares no
/// <see cref="WorkflowNode.IdempotencyKeyExpression"/> is never retried more than once, regardless
/// of policy (FR-040); one that does declare an expression instead checks <see
/// cref="WorkflowExecutionNode.ResolvedIdempotencyKey"/> history for this execution before
/// re-invoking — a match reuses the prior output rather than calling the capability again
/// (research.md Decision 13). Once a node's own retries are exhausted, <see
/// cref="Domain.Workflows.WorkflowVersion.ErrorPolicyJson"/>'s workflow-level strategy (FR-039)
/// governs what happens next: <c>Stop</c> (default) fails the execution; <c>Continue</c> proceeds
/// past the failed node down its normal outgoing connection with no output resolved for it;
/// <c>Retry</c> grants exactly one additional attempt beyond the node's own policy before falling
/// back to <c>Stop</c> semantics; <c>Fallback</c> reuses the failed node's own <see
/// cref="WorkflowNode.CompensatingNodeId"/> as an alternate node to run in its place (a deliberate,
/// documented reuse of that single field — under <c>Compensate</c> the same field means "clean up
/// this node if a later node fails," under <c>Fallback</c> it means "run this instead of me if I
/// fail" — spec.md/research.md define no second field for this, and the two strategies are never
/// both active for the same failure); <c>Compensate</c> walks already-<see
/// cref="WorkflowExecutionNodeStatus.Completed"/> nodes in reverse execution order and runs each
/// one's own <see cref="WorkflowNode.CompensatingNodeId"/> (through this same node-dispatch path,
/// best-effort) before still failing the execution (research.md Decision 14). Parallel-node
/// branches are explicitly out of scope for retry/timeout/failure-strategy in this pass — their
/// existing Merge-strategy tolerance (<c>AnyCompleted</c>/<c>FirstCompleted</c>) is the only
/// resilience mechanism for a branch failure; layering per-branch retry onto the already-concurrent
/// dispatch was judged too large a change for this story's scope.</para>
/// </summary>
public sealed class WorkflowExecutionOrchestrator(
    IWorkflowExecutionRepository executionRepository,
    IWorkflowRepository workflowRepository,
    WorkflowNodeExecutorRegistry executorRegistry,
    IWorkflowExpressionEvaluator expressionEvaluator,
    WorkflowBudgetGuard budgetGuard,
    WorkflowPolicyEvaluator policyEvaluator,
    AgentToolCatalog toolCatalog,
    IWorkflowExecutionNotifier notifier,
    IWorkflowAuditLogRepository auditLogRepository,
    IUnitOfWork unitOfWork)
{
    private sealed record ParallelExecutionOutcome(bool Succeeded, WorkflowNode? MergeNode, string? FailureReason);

    public async Task RunAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        var execution = await executionRepository.GetByIdAsync(executionId, cancellationToken)
            ?? throw new KeyNotFoundException("Workflow execution not found.");

        if (execution.Status is WorkflowExecutionStatus.Completed or WorkflowExecutionStatus.Failed or WorkflowExecutionStatus.Cancelled or WorkflowExecutionStatus.TimedOut)
        {
            return; // Already terminal — a defensive guard against a duplicate/racing enqueue.
        }

        try
        {
            var version = await workflowRepository.GetVersionByIdAsync(execution.WorkflowVersionId, cancellationToken)
                ?? throw new InvalidOperationException("The workflow version this execution ran under no longer exists.");

            var isResuming = execution.Nodes.Count > 0;

            var nodesById = version.Nodes.ToDictionary(n => n.Id);
            var connections = version.Connections.ToList();
            var executionPolicy = WorkflowExecutionPolicyParser.Parse(version.ExecutionPolicyJson);
            var errorPolicy = WorkflowErrorPolicyParser.Parse(version.ErrorPolicyJson);
            var loopBodyFirstNodeIds = connections.Where(c => c.BranchLabel == WorkflowConnection.LoopBackBranchLabel).Select(c => c.TargetNodeId).ToHashSet();

            var resolvedValues = new Dictionary<string, WorkflowExpressionValue>(StringComparer.Ordinal);
            WorkflowResolvedValues.AddFlattened(resolvedValues, "workflow", execution.InputsJson);

            var skippedNodeIds = new HashSet<Guid>();
            var loopIterationCounts = new Dictionary<Guid, int>();
            var nodeCount = 0;
            string? finalOutputJson = null;
            WorkflowNode current;
            WorkflowExecutionNode? nodeRowToReuse = null;

            if (isResuming)
            {
                foreach (var executionNode in execution.Nodes)
                {
                    var historicNode = nodesById[executionNode.WorkflowNodeId];
                    nodeCount++;

                    if (executionNode.Status == WorkflowExecutionNodeStatus.Skipped)
                    {
                        skippedNodeIds.Add(historicNode.Id);
                    }
                    else if (executionNode.Status == WorkflowExecutionNodeStatus.Completed)
                    {
                        if (historicNode.NodeType == WorkflowNodeType.End)
                        {
                            finalOutputJson = executionNode.OutputJson;
                        }
                        else
                        {
                            WorkflowResolvedValues.AddFlattened(resolvedValues, $"steps.{historicNode.NodeKey}", executionNode.OutputJson);
                        }

                        if (loopBodyFirstNodeIds.Contains(historicNode.Id))
                        {
                            loopIterationCounts[historicNode.Id] = loopIterationCounts.GetValueOrDefault(historicNode.Id, 0) + 1;
                        }
                    }
                }

                var waitingNode = execution.Nodes.FirstOrDefault(n => n.Status == WorkflowExecutionNodeStatus.WaitingForApproval);
                if (waitingNode is not null)
                {
                    var approval = execution.Approvals.First(a => a.WorkflowExecutionNodeId == waitingNode.Id);
                    if (approval.Decision == WorkflowApprovalDecision.Pending)
                    {
                        if (approval.TimeoutSeconds is { } timeoutSeconds && (DateTime.UtcNow - approval.CreatedAtUtc).TotalSeconds > timeoutSeconds)
                        {
                            var timedOutNode = nodesById[waitingNode.WorkflowNodeId];
                            approval.CancelByTimeout();
                            waitingNode.Fail();
                            var timeoutError = execution.RecordError(
                                WorkflowErrorCategory.Timeout, $"Node '{timedOutNode.NodeKey}' timed out waiting for approval after {timeoutSeconds}s.", waitingNode.Id, retryCount: 0);
                            execution.Fail(timeoutError.Message);
                            execution.RecordEvent(WorkflowExecutionEventType.WorkflowFailed, workflowNodeId: null, "Failed", null);
                            await unitOfWork.SaveChangesAsync(cancellationToken);
                        }

                        return; // Still legitimately waiting (or just timed out above) — nothing further to do this run.
                    }

                    if (approval.Decision != WorkflowApprovalDecision.Approve)
                    {
                        // Reject/RequestChanges already terminated the execution directly in their own
                        // command handlers, without re-enqueuing the runner — RunAsync should never
                        // observe a non-pending, non-Approve decision.
                        return;
                    }

                    current = nodesById[waitingNode.WorkflowNodeId];
                    nodeRowToReuse = waitingNode;
                }
                else
                {
                    // spec.md User Story 7 — RetryWorkflowExecutionNodeCommand resets a Failed
                    // node's row to Pending (and the execution back to Running) rather than
                    // restarting the whole graph; resume at that same reused row, mirroring the
                    // WaitingForApproval resume path exactly (same nodeRowToReuse mechanism).
                    var retryNode = execution.Nodes.FirstOrDefault(n => n.Status == WorkflowExecutionNodeStatus.Pending)
                        ?? throw new InvalidOperationException("This execution has no node waiting for approval or reset for retry to resume from.");

                    current = nodesById[retryNode.WorkflowNodeId];
                    nodeRowToReuse = retryNode;
                }
            }
            else
            {
                current = version.Nodes.FirstOrDefault(n => n.NodeType == WorkflowNodeType.Start)
                    ?? throw new InvalidOperationException("The workflow version has no Start node.");
            }

            // Deferred until we know we're actually proceeding (fresh start, or an approved resume)
            // — calling this unconditionally at the top would flip Status to Running even when this
            // invocation turns out to just be re-observing a still-Pending, not-yet-timed-out
            // approval and returns without doing anything, leaving the execution stuck showing
            // Running instead of its true WaitingForApproval state.
            execution.Start();
            await unitOfWork.SaveChangesAsync(cancellationToken);
            if (!isResuming)
            {
                await RecordAndNotifyAsync(
                    execution, WorkflowExecutionEventType.WorkflowStarted, workflowNodeId: null, "Running", null,
                    () => notifier.NotifyWorkflowStartedAsync(execution.RunByUserId, execution.Id, execution.WorkflowId, DateTime.UtcNow, cancellationToken),
                    cancellationToken);
            }

            while (true)
            {
                // FR-048/SC-007 — observed at every node boundary, not continuously: a pause/cancel
                // requested from a concurrent HTTP request (PauseWorkflowExecutionCommand/
                // CancelWorkflowExecutionCommand) already persisted the correct status itself via
                // its own tracked entity, so this run exits immediately without touching (or
                // re-saving) its own now-stale in-memory `execution` — doing so would either
                // overwrite that status or throw a spurious concurrency conflict.
                var externalStatus = await executionRepository.GetStatusAsync(execution.Id, cancellationToken);
                if (externalStatus is WorkflowExecutionStatus.Paused or WorkflowExecutionStatus.Cancelled)
                {
                    return;
                }

                var executionNodeForThisIteration = nodeRowToReuse;
                nodeRowToReuse = null;

                if (skippedNodeIds.Contains(current.Id))
                {
                    var skipForward = connections.FirstOrDefault(c => c.SourceNodeId == current.Id && c.BranchLabel != WorkflowConnection.LoopBackBranchLabel);
                    if (skipForward is null)
                    {
                        break;
                    }

                    current = nodesById[skipForward.TargetNodeId];
                    continue;
                }

                if (executionNodeForThisIteration is null)
                {
                    nodeCount++;
                }

                var budgetCheck = budgetGuard.Check(
                    executionPolicy, execution.StartedAtUtc!.Value, nodeCount, toolCallCount: 0,
                    execution.Usage?.InputTokenCount, execution.Usage?.OutputTokenCount, execution.Cost?.EstimatedCost);
                if (budgetCheck.IsExceeded)
                {
                    execution.RecordError(WorkflowErrorCategory.BudgetExceeded, budgetCheck.Reason!, workflowExecutionNodeId: null, retryCount: 0);
                    execution.Fail(budgetCheck.Reason!);
                    await RecordAndNotifyAsync(
                        execution, WorkflowExecutionEventType.WorkflowFailed, workflowNodeId: null, "Failed", null,
                        () => notifier.NotifyWorkflowFailedAsync(execution.RunByUserId, execution.Id, execution.TerminationReason ?? "The execution failed.", DateTime.UtcNow, cancellationToken),
                        cancellationToken);
                    return;
                }

                if (executionNodeForThisIteration is null && loopBodyFirstNodeIds.Contains(current.Id))
                {
                    var iterationsSoFar = loopIterationCounts.GetValueOrDefault(current.Id, 0);
                    var maxIterations = ExtractMaxIterations(current.ConfigurationJson);
                    var loopCheck = budgetGuard.CheckLoopIteration(executionPolicy, maxIterations, iterationsSoFar);

                    if (loopCheck.IsExceeded)
                    {
                        var exitConnection = connections.FirstOrDefault(c => c.SourceNodeId == current.Id && c.BranchLabel != WorkflowConnection.LoopBackBranchLabel);
                        if (exitConnection is null)
                        {
                            var error = execution.RecordError(WorkflowErrorCategory.BudgetExceeded, loopCheck.Reason!, workflowExecutionNodeId: null, retryCount: 0);
                            execution.Fail(error.Message);
                            await RecordAndNotifyAsync(
                                execution, WorkflowExecutionEventType.WorkflowFailed, workflowNodeId: null, "Failed", null,
                                () => notifier.NotifyWorkflowFailedAsync(execution.RunByUserId, execution.Id, execution.TerminationReason ?? "The execution failed.", DateTime.UtcNow, cancellationToken),
                                cancellationToken);
                            return;
                        }

                        current = nodesById[exitConnection.TargetNodeId];
                        continue;
                    }

                    loopIterationCounts[current.Id] = iterationsSoFar + 1;
                }

                if (executionNodeForThisIteration is null)
                {
                    var gate = await EvaluateApprovalGateAsync(execution, current, resolvedValues, cancellationToken);
                    if (gate.Paused)
                    {
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        return;
                    }

                    executionNodeForThisIteration = gate.AutoApprovedExecutionNode;
                }

                if (current.NodeType == WorkflowNodeType.Parallel)
                {
                    var parallelOutcome = await ExecuteParallelAsync(execution, current, resolvedValues, executionPolicy, nodesById, connections, cancellationToken);
                    if (!parallelOutcome.Succeeded)
                    {
                        var error = execution.RecordError(WorkflowErrorCategory.NodeExecutionFailure, parallelOutcome.FailureReason ?? "The Parallel node did not complete successfully.", workflowExecutionNodeId: null, retryCount: 0);
                        execution.Fail(error.Message);
                        await RecordAndNotifyAsync(
                            execution, WorkflowExecutionEventType.WorkflowFailed, workflowNodeId: null, "Failed", null,
                            () => notifier.NotifyWorkflowFailedAsync(execution.RunByUserId, execution.Id, execution.TerminationReason ?? "The execution failed.", DateTime.UtcNow, cancellationToken),
                            cancellationToken);
                        return;
                    }

                    current = parallelOutcome.MergeNode!;
                    continue;
                }

                var (nodeSucceeded, outputJson, failureReason, failureCategory) = await ExecuteSingleNodeAsync(execution, current, executionNodeForThisIteration, resolvedValues, errorPolicy, cancellationToken);
                if (!nodeSucceeded)
                {
                    var error = execution.RecordError(failureCategory, failureReason ?? "The node did not complete successfully.", workflowExecutionNodeId: null, retryCount: 0);

                    if (errorPolicy.EffectiveStrategy == "Compensate")
                    {
                        await RunCompensationsAsync(execution, nodesById, resolvedValues, cancellationToken);
                        execution.Fail($"Node '{current.NodeKey}' failed: {error.Message}");
                        await RecordAndNotifyAsync(
                            execution, WorkflowExecutionEventType.WorkflowFailed, workflowNodeId: null, "Failed", null,
                            () => notifier.NotifyWorkflowFailedAsync(execution.RunByUserId, execution.Id, execution.TerminationReason ?? "The execution failed.", DateTime.UtcNow, cancellationToken),
                            cancellationToken);
                        return;
                    }

                    if (errorPolicy.EffectiveStrategy is "Continue" or "Fallback")
                    {
                        // FR-039 — the execution tolerates this node's failure and proceeds, rather
                        // than stopping. Fallback additionally runs the failed node's own
                        // CompensatingNodeId (a deliberate reuse of that field — see the class doc
                        // comment) in its place when one is configured; otherwise it behaves exactly
                        // like Continue.
                        if (errorPolicy.EffectiveStrategy == "Fallback" && current.CompensatingNodeId is { } fallbackNodeId)
                        {
                            current = nodesById[fallbackNodeId];
                            continue;
                        }

                        var pastFailure = connections.FirstOrDefault(c => c.SourceNodeId == current.Id && c.BranchLabel != WorkflowConnection.LoopBackBranchLabel);
                        if (pastFailure is null)
                        {
                            break; // Nothing downstream — falls through to the normal Completed finalization below.
                        }

                        current = nodesById[pastFailure.TargetNodeId];
                        continue;
                    }

                    // Stop (default) and any unrecognized strategy.
                    execution.Fail($"Node '{current.NodeKey}' failed: {error.Message}");
                    await RecordAndNotifyAsync(
                        execution, WorkflowExecutionEventType.WorkflowFailed, workflowNodeId: null, "Failed", null,
                        () => notifier.NotifyWorkflowFailedAsync(execution.RunByUserId, execution.Id, execution.TerminationReason ?? "The execution failed.", DateTime.UtcNow, cancellationToken),
                        cancellationToken);
                    return;
                }

                if (current.NodeType == WorkflowNodeType.End)
                {
                    finalOutputJson = outputJson;
                    break;
                }

                WorkflowResolvedValues.AddFlattened(resolvedValues, $"steps.{current.NodeKey}", outputJson);

                if (current.NodeType == WorkflowNodeType.Condition)
                {
                    var chosenLabel = resolvedValues.TryGetValue($"steps.{current.NodeKey}.result", out var conditionValue) && conditionValue.BooleanValue == true
                        ? "true"
                        : "false";
                    var skippedLabel = chosenLabel == "true" ? "false" : "true";

                    var chosenConnection = connections.FirstOrDefault(c => c.SourceNodeId == current.Id && c.BranchLabel == chosenLabel);
                    if (chosenConnection is null)
                    {
                        var error = execution.RecordError(WorkflowErrorCategory.NodeExecutionFailure, $"Condition node '{current.NodeKey}' has no outgoing connection labeled '{chosenLabel}'.", workflowExecutionNodeId: null, retryCount: 0);
                        execution.Fail(error.Message);
                        await RecordAndNotifyAsync(
                            execution, WorkflowExecutionEventType.WorkflowFailed, workflowNodeId: null, "Failed", null,
                            () => notifier.NotifyWorkflowFailedAsync(execution.RunByUserId, execution.Id, execution.TerminationReason ?? "The execution failed.", DateTime.UtcNow, cancellationToken),
                            cancellationToken);
                        return;
                    }

                    var skippedConnection = connections.FirstOrDefault(c => c.SourceNodeId == current.Id && c.BranchLabel == skippedLabel);
                    if (skippedConnection is not null)
                    {
                        MarkExclusivelyReachableAsSkipped(execution, skippedConnection.TargetNodeId, chosenConnection.TargetNodeId, connections, skippedNodeIds);
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                    }

                    current = nodesById[chosenConnection.TargetNodeId];
                    continue;
                }

                // Iteration budgeting already happened up-front (loopBodyFirstNodeIds check, above)
                // before this node was even executed — a loop-back edge here is always taken
                // unconditionally; the guard already redirected to the exit path instead of
                // executing the body at all once the cap was reached.
                var loopBack = connections.FirstOrDefault(c => c.SourceNodeId == current.Id && c.BranchLabel == WorkflowConnection.LoopBackBranchLabel);
                if (loopBack is not null)
                {
                    current = nodesById[loopBack.TargetNodeId];
                    continue;
                }

                var next = connections.FirstOrDefault(c => c.SourceNodeId == current.Id && c.BranchLabel != WorkflowConnection.LoopBackBranchLabel);
                if (next is null)
                {
                    break;
                }

                current = nodesById[next.TargetNodeId];
            }

            execution.SetVariables(WorkflowResolvedValues.ToInputDocument(resolvedValues).RootElement.GetRawText());
            execution.Complete(finalOutputJson);
            auditLogRepository.Add(WorkflowAuditLog.Create(execution.WorkflowId, execution.Id, execution.RunByUserId, WorkflowAuditAction.ExecutionCompleted, "{}"));
            await RecordAndNotifyAsync(
                execution, WorkflowExecutionEventType.WorkflowCompleted, workflowNodeId: null, "Completed", null,
                () => notifier.NotifyWorkflowCompletedAsync(execution.RunByUserId, execution.Id, DateTime.UtcNow, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex)
        {
            execution.RecordError(CategorizeFailure(ex), SafeFailureMessage(ex), workflowExecutionNodeId: null, retryCount: 0);
            execution.Fail(SafeFailureMessage(ex));
            auditLogRepository.Add(WorkflowAuditLog.Create(execution.WorkflowId, execution.Id, execution.RunByUserId, WorkflowAuditAction.ExecutionFailed, "{}"));
            if (ex is KeyNotFoundException)
            {
                // A node's own ownership guard (e.g. a document/knowledge-base lookup inside a
                // capability node) threw — the execution never held a permission/resource it
                // needed (mirrors AgentExecutionOrchestrator's identical PermissionDenied placement).
                auditLogRepository.Add(WorkflowAuditLog.Create(execution.WorkflowId, execution.Id, execution.RunByUserId, WorkflowAuditAction.PermissionDenied, "{}"));
            }

            await RecordAndNotifyAsync(
                execution, WorkflowExecutionEventType.WorkflowFailed, workflowNodeId: null, "Failed", null,
                () => notifier.NotifyWorkflowFailedAsync(execution.RunByUserId, execution.Id, execution.TerminationReason ?? "The execution failed.", DateTime.UtcNow, cancellationToken),
                cancellationToken);
        }
    }

    /// <summary>FR-049/contracts/workflow-execution-events.md — persists a <see cref="WorkflowExecutionEvent"/> row (append-only, safe-metadata-only per FR-053), then pushes the corresponding live payload over <see cref="IWorkflowExecutionNotifier"/>. The persisted row is always the reconciliation source of truth if the live push is missed.</summary>
    private async Task RecordAndNotifyAsync(
        WorkflowExecution execution, WorkflowExecutionEventType eventType, Guid? workflowNodeId, string status, string? safeMetadataJson,
        Func<Task> notify, CancellationToken cancellationToken)
    {
        execution.RecordEvent(eventType, workflowNodeId, status, safeMetadataJson);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notify();
    }

    private sealed record ApprovalGateOutcome(bool Paused, WorkflowExecutionNode? AutoApprovedExecutionNode)
    {
        public static readonly ApprovalGateOutcome NotRequired = new(false, null);
    }

    /// <summary>
    /// FR-033-FR-036 — decides whether <paramref name="node"/> requires a pause before it runs.
    /// Never called for a node already resumed from a prior pause (its decision is already known).
    /// </summary>
    private async Task<ApprovalGateOutcome> EvaluateApprovalGateAsync(
        WorkflowExecution execution, WorkflowNode node, Dictionary<string, WorkflowExpressionValue> resolvedValues, CancellationToken cancellationToken)
    {
        var isHumanApprovalNode = node.NodeType == WorkflowNodeType.HumanApproval;
        var underlyingToolName = TryResolveUnderlyingToolName(node);
        var underlyingRiskLevel = underlyingToolName is not null ? toolCatalog.Find(underlyingToolName)?.RiskLevel : null;
        var mandatoryBaseline = isHumanApprovalNode || underlyingRiskLevel is AgentToolRiskLevel.High or AgentToolRiskLevel.Critical;
        var nodeForcesApproval = node.ApprovalPolicy is WorkflowNodeApprovalPolicy.AlwaysRequire or WorkflowNodeApprovalPolicy.AboveRiskLevel or WorkflowNodeApprovalPolicy.ForThisNodeType;

        if (!mandatoryBaseline && !nodeForcesApproval)
        {
            return ApprovalGateOutcome.NotRequired;
        }

        var intendedAction = isHumanApprovalNode
            ? $"Proceed past the '{node.NodeKey}' approval step"
            : $"Execute {underlyingToolName ?? node.NodeType.ToString()} at step '{node.NodeKey}'";
        var parametersJson = WorkflowResolvedValues.ToInputDocument(resolvedValues).RootElement.GetRawText();

        // A node-level policy the author explicitly opted into (never bypassable by an admin
        // policy — that would defeat the author's own stricter choice) never checks for an
        // auto-approval match; only the platform's own risk-based baseline can be matched.
        var matchedPolicy = mandatoryBaseline
            ? await policyEvaluator.FindMatchAsync(node.NodeType, underlyingToolName, parametersJson, cancellationToken)
            : null;

        var executionNode = execution.AddNode(node.Id);
        executionNode.Start(inputJson: null);
        await RecordAndNotifyAsync(
            execution, WorkflowExecutionEventType.NodeStarted, node.Id, "Running", null,
            () => notifier.NotifyNodeStartedAsync(execution.RunByUserId, execution.Id, node.Id, DateTime.UtcNow, cancellationToken),
            cancellationToken);

        if (matchedPolicy is null)
        {
            executionNode.WaitForApproval();
            var approval = execution.RequestApproval(executionNode.Id, intendedAction, parametersJson, node.TimeoutSeconds);
            await RecordAndNotifyAsync(
                execution, WorkflowExecutionEventType.ApprovalRequested, node.Id, "WaitingForApproval", null,
                () => notifier.NotifyApprovalRequestedAsync(execution.RunByUserId, execution.Id, node.Id, approval.Id, intendedAction, DateTime.UtcNow, cancellationToken),
                cancellationToken);
            return new ApprovalGateOutcome(true, null);
        }

        var autoApproval = execution.RequestApproval(executionNode.Id, intendedAction, parametersJson, node.TimeoutSeconds);
        autoApproval.ApproveByPolicy(matchedPolicy.Id);
        execution.Resume();
        execution.RecordEvent(WorkflowExecutionEventType.ApprovalGranted, node.Id, "Approved", null);
        return new ApprovalGateOutcome(false, executionNode);
    }

    /// <summary>
    /// The fixed (or config-derived) <see cref="IAgentTool.Name"/> each thin-adapter capability
    /// node type ultimately calls — mirrors exactly what each executor (<c>RagSearchNodeExecutor</c>
    /// etc.) itself resolves, duplicated here only for the risk-level lookup this approval gate
    /// needs <i>before</i> the node actually runs (an <see cref="IWorkflowNodeExecutor"/> has no
    /// way to report this back other than by actually executing).
    /// </summary>
    private static string? TryResolveUnderlyingToolName(WorkflowNode node)
    {
        switch (node.NodeType)
        {
            case WorkflowNodeType.RagSearch:
                return "KnowledgeSearchTool";
            case WorkflowNodeType.MemorySearch:
                return "MemorySearchTool";
            case WorkflowNodeType.DocumentProcessing:
                return "DocumentSearchTool";
            case WorkflowNodeType.FileOperation:
                try
                {
                    using var configuration = JsonDocument.Parse(node.ConfigurationJson);
                    var operation = configuration.RootElement.TryGetProperty("operation", out var operationElement) && operationElement.ValueKind == JsonValueKind.String
                        ? operationElement.GetString()
                        : "Read";
                    return operation?.Equals("Metadata", StringComparison.OrdinalIgnoreCase) == true ? "FileMetadataTool" : "FileReadTool";
                }
                catch (JsonException)
                {
                    return "FileReadTool";
                }
            case WorkflowNodeType.McpTool:
            case WorkflowNodeType.NativeTool:
                try
                {
                    using var configuration = JsonDocument.Parse(node.ConfigurationJson);
                    return configuration.RootElement.TryGetProperty("toolName", out var toolNameElement) && toolNameElement.ValueKind == JsonValueKind.String
                        ? toolNameElement.GetString()
                        : null;
                }
                catch (JsonException)
                {
                    return null;
                }
            default:
                return null;
        }
    }

    /// <summary>research.md Decision 13 — the exact permission set that makes a node's underlying capability "mutating" (the point at which idempotency/single-retry rules apply), mirrors <c>AgentExecutionOrchestrator.MutatingPermissions</c> exactly.</summary>
    private static readonly HashSet<AgentToolPermission> MutatingPermissions =
    [
        AgentToolPermission.WriteFile, AgentToolPermission.ModifyData, AgentToolPermission.SendEmail,
        AgentToolPermission.ExecuteCode, AgentToolPermission.HighRiskOperation,
    ];

    private bool IsMutatingNode(WorkflowNode node)
    {
        var toolName = TryResolveUnderlyingToolName(node);
        var tool = toolName is not null ? toolCatalog.Find(toolName) : null;
        return tool is not null && tool.RequiredPermissions.Any(MutatingPermissions.Contains);
    }

    /// <summary>
    /// Runs one node end-to-end (create-or-reuse/start/retry-loop/complete-or-fail the <see
    /// cref="WorkflowExecutionNode"/> row), used for every node type except <see
    /// cref="WorkflowNodeType.Parallel"/> (which has its own multi-node bookkeeping) — see the
    /// class doc comment for the full retry/timeout/idempotency/failure-strategy design (FR-039-FR-043).
    /// </summary>
    private async Task<(bool Succeeded, string? OutputJson, string? FailureReason, WorkflowErrorCategory FailureCategory)> ExecuteSingleNodeAsync(
        WorkflowExecution execution, WorkflowNode node, WorkflowExecutionNode? existingExecutionNode, Dictionary<string, WorkflowExpressionValue> resolvedValues,
        WorkflowErrorPolicy errorPolicy, CancellationToken cancellationToken)
    {
        var executionNode = existingExecutionNode ?? execution.AddNode(node.Id);
        executionNode.Start(inputJson: null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (existingExecutionNode is null)
        {
            await RecordAndNotifyAsync(
                execution, WorkflowExecutionEventType.NodeStarted, node.Id, "Running", null,
                () => notifier.NotifyNodeStartedAsync(execution.RunByUserId, execution.Id, node.Id, DateTime.UtcNow, cancellationToken),
                cancellationToken);
        }

        var isMutating = IsMutatingNode(node);
        string? resolvedIdempotencyKey = null;
        if (isMutating && node.IdempotencyKeyExpression is { } keyExpression)
        {
            resolvedIdempotencyKey = expressionEvaluator.Evaluate(expressionEvaluator.Parse(keyExpression), resolvedValues).ToDisplayString();
            var priorAttempt = execution.Nodes.FirstOrDefault(
                n => n.WorkflowNodeId == node.Id && n.Status == WorkflowExecutionNodeStatus.Completed && n.ResolvedIdempotencyKey == resolvedIdempotencyKey);
            if (priorAttempt is not null)
            {
                executionNode.Complete(priorAttempt.OutputJson, resolvedIdempotencyKey);
                await RecordAndNotifyAsync(
                    execution, WorkflowExecutionEventType.NodeCompleted, node.Id, "Completed", null,
                    () => notifier.NotifyNodeCompletedAsync(execution.RunByUserId, execution.Id, node.Id, DateTime.UtcNow, cancellationToken),
                    cancellationToken);
                return (true, priorAttempt.OutputJson, null, default);
            }
        }

        var retryPolicy = WorkflowNodeRetryPolicyParser.Parse(node.RetryPolicyJson);
        // FR-040 — a mutating operation with no idempotency key can never be safely retried,
        // regardless of the node's own configured policy or a workflow-level Retry strategy.
        var maxAttempts = isMutating && resolvedIdempotencyKey is null
            ? 1
            : (retryPolicy.MaxAttempts ?? 1) + (errorPolicy.EffectiveStrategy == "Retry" ? 1 : 0);

        string? failureReason = null;
        var failureCategory = WorkflowErrorCategory.NodeExecutionFailure;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                var delay = ComputeRetryDelay(retryPolicy, attempt - 1);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }
            }

            var timeoutSeconds = budgetGuard.ResolveNodeTimeoutSeconds(node.TimeoutSeconds);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            WorkflowNodeExecutionResult result;
            try
            {
                result = await ExecuteNodeAsync(execution, executionNode, node, resolvedValues, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The linked token fired from CancelAfter, not the caller's own token — a genuine
                // per-node timeout (FR-041), not an outer cancellation request.
                failureReason = $"Node '{node.NodeKey}' timed out after {timeoutSeconds}s.";
                failureCategory = WorkflowErrorCategory.Timeout;

                if (attempt < maxAttempts && IsRetryableFailure(retryPolicy, failureCategory))
                {
                    executionNode.IncrementRetryCount();
                    await RecordAndNotifyAsync(
                        execution, WorkflowExecutionEventType.NodeRetrying, node.Id, "Running", null,
                        () => notifier.NotifyNodeRetryingAsync(execution.RunByUserId, execution.Id, node.Id, executionNode.RetryCount, DateTime.UtcNow, cancellationToken),
                        cancellationToken);
                    continue;
                }

                break;
            }

            if (result.Succeeded)
            {
                var outputJson = result.Output?.RootElement.GetRawText();
                executionNode.Complete(outputJson, resolvedIdempotencyKey);
                await RecordAndNotifyAsync(
                    execution, WorkflowExecutionEventType.NodeCompleted, node.Id, "Completed", null,
                    () => notifier.NotifyNodeCompletedAsync(execution.RunByUserId, execution.Id, node.Id, DateTime.UtcNow, cancellationToken),
                    cancellationToken);
                return (true, outputJson, null, default);
            }

            failureReason = result.FailureReason;
            failureCategory = WorkflowErrorCategory.NodeExecutionFailure;

            if (attempt < maxAttempts && IsRetryableFailure(retryPolicy, failureCategory))
            {
                executionNode.IncrementRetryCount();
                await RecordAndNotifyAsync(
                    execution, WorkflowExecutionEventType.NodeRetrying, node.Id, "Running", null,
                    () => notifier.NotifyNodeRetryingAsync(execution.RunByUserId, execution.Id, node.Id, executionNode.RetryCount, DateTime.UtcNow, cancellationToken),
                    cancellationToken);
                continue;
            }

            break;
        }

        executionNode.Fail();
        var finalFailureReason = failureReason ?? "The node did not complete successfully.";
        await RecordAndNotifyAsync(
            execution, WorkflowExecutionEventType.NodeFailed, node.Id, "Failed", null,
            () => notifier.NotifyNodeFailedAsync(execution.RunByUserId, execution.Id, node.Id, finalFailureReason, DateTime.UtcNow, cancellationToken),
            cancellationToken);
        return (false, null, finalFailureReason, failureCategory);
    }

    /// <summary>FR-040 — exponential (default)/linear/fixed backoff, capped by the policy's own <c>MaxDelaySeconds</c> (default 30s if unset). <paramref name="retryAttemptNumber"/> is 1-based (the delay before the 2nd overall attempt is retry attempt 1).</summary>
    private static TimeSpan ComputeRetryDelay(WorkflowRetryPolicy policy, int retryAttemptNumber)
    {
        var initialSeconds = policy.InitialDelaySeconds ?? 1;
        var maxSeconds = policy.MaxDelaySeconds ?? 30;
        var seconds = policy.BackoffStrategy switch
        {
            "Fixed" => (double)initialSeconds,
            "Linear" => initialSeconds * (double)retryAttemptNumber,
            _ => initialSeconds * Math.Pow(2, retryAttemptNumber - 1), // Exponential — the default when unset.
        };

        return TimeSpan.FromSeconds(Math.Min(seconds, maxSeconds));
    }

    /// <summary>FR-040 — an explicit <c>NonRetryableErrorTypesJson</c> entry always wins; otherwise an explicit <c>RetryableErrorTypesJson</c> acts as an allowlist; with neither configured, every category is retryable by default.</summary>
    private static bool IsRetryableFailure(WorkflowRetryPolicy policy, WorkflowErrorCategory category)
    {
        var categoryName = category.ToString();

        if (TryParseErrorTypeList(policy.NonRetryableErrorTypesJson) is { } nonRetryable && nonRetryable.Contains(categoryName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryParseErrorTypeList(policy.RetryableErrorTypesJson) is { } retryable)
        {
            return retryable.Contains(categoryName, StringComparer.OrdinalIgnoreCase);
        }

        return true;
    }

    private static string[]? TryParseErrorTypeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>research.md Decision 14/FR-042 — walks already-<see cref="WorkflowExecutionNodeStatus.Completed"/> nodes in reverse execution order and runs each one's own explicitly configured <see cref="WorkflowNode.CompensatingNodeId"/> (never inferred) through the same node-dispatch path as normal execution. Best-effort: a compensating node that itself fails is recorded but does not block compensating the rest, and is never itself subject to a further round of compensation.</summary>
    private async Task RunCompensationsAsync(
        WorkflowExecution execution, Dictionary<Guid, WorkflowNode> nodesById, Dictionary<string, WorkflowExpressionValue> resolvedValues, CancellationToken cancellationToken)
    {
        var completedNodes = execution.Nodes.Where(n => n.Status == WorkflowExecutionNodeStatus.Completed).Reverse().ToList();
        foreach (var completedNode in completedNodes)
        {
            if (!nodesById.TryGetValue(completedNode.WorkflowNodeId, out var definitionNode) || definitionNode.CompensatingNodeId is not { } compensatingNodeId)
            {
                continue;
            }

            var compensatingNode = nodesById[compensatingNodeId];
            var (succeeded, _, failureReason, _) = await ExecuteSingleNodeAsync(execution, compensatingNode, null, resolvedValues, WorkflowErrorPolicy.Empty, cancellationToken);
            if (!succeeded)
            {
                execution.RecordError(WorkflowErrorCategory.NodeExecutionFailure, $"Compensating node '{compensatingNode.NodeKey}' failed: {failureReason}", workflowExecutionNodeId: null, retryCount: 0);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<WorkflowNodeExecutionResult> ExecuteNodeAsync(
        WorkflowExecution execution, WorkflowExecutionNode executionNode, WorkflowNode node,
        Dictionary<string, WorkflowExpressionValue> resolvedValues, CancellationToken cancellationToken)
    {
        if (node.NodeType == WorkflowNodeType.Start)
        {
            return WorkflowNodeExecutionResult.Success(JsonDocument.Parse(execution.InputsJson));
        }

        if (node.NodeType == WorkflowNodeType.End)
        {
            return EvaluateEndNodeOutputs(node, resolvedValues);
        }

        if (node.NodeType == WorkflowNodeType.HumanApproval)
        {
            // The approval gate already ran (and, if we're here, already resolved to Approve) —
            // this node has no capability of its own to invoke, it is purely a pause point.
            return WorkflowNodeExecutionResult.Success(JsonDocument.Parse("{}"));
        }

        var executor = executorRegistry.Find(node.NodeType);
        if (executor is null)
        {
            return WorkflowNodeExecutionResult.Failure($"No executor is registered for node type '{node.NodeType}'.");
        }

        var context = new WorkflowNodeExecutionContext(execution.Id, executionNode.Id, execution.RunByUserId, execution.WorkflowId, execution.WorkflowVersionId, node);
        using var input = WorkflowResolvedValues.ToInputDocument(resolvedValues);
        return await executor.ExecuteAsync(context, input, cancellationToken);
    }

    /// <summary>
    /// FR-030/FR-031, research.md Decision 9 — fans a Parallel node's branches out concurrently
    /// (each branch's <see cref="IWorkflowNodeExecutor.ExecuteAsync"/> call only; nothing here
    /// touches the shared <see cref="WorkflowExecution"/> aggregate or <see cref="IUnitOfWork"/>
    /// concurrently — that bookkeeping happens sequentially afterward, since neither is
    /// thread-safe), converging at a single Merge node. Only single-node branches are supported;
    /// see the class doc comment.
    /// </summary>
    private async Task<ParallelExecutionOutcome> ExecuteParallelAsync(
        WorkflowExecution execution, WorkflowNode parallelNode, Dictionary<string, WorkflowExpressionValue> resolvedValues,
        WorkflowExecutionPolicy executionPolicy, Dictionary<Guid, WorkflowNode> nodesById, IReadOnlyList<WorkflowConnection> connections,
        CancellationToken cancellationToken)
    {
        var branchConnections = connections.Where(c => c.SourceNodeId == parallelNode.Id && c.BranchLabel != WorkflowConnection.LoopBackBranchLabel).ToList();
        if (branchConnections.Count == 0)
        {
            return new ParallelExecutionOutcome(false, null, $"Parallel node '{parallelNode.NodeKey}' has no outgoing branches.");
        }

        var branchNodes = new List<WorkflowNode>();
        WorkflowNode? mergeNode = null;
        foreach (var connection in branchConnections)
        {
            var branchNode = nodesById[connection.TargetNodeId];
            if (branchNode.NodeType == WorkflowNodeType.Merge)
            {
                return new ParallelExecutionOutcome(false, null, $"Parallel node '{parallelNode.NodeKey}' has a branch connecting directly to a Merge node with no work in between.");
            }

            var forward = connections.FirstOrDefault(c => c.SourceNodeId == branchNode.Id && c.BranchLabel != WorkflowConnection.LoopBackBranchLabel);
            if (forward is null || !nodesById.TryGetValue(forward.TargetNodeId, out var branchMergeNode) || branchMergeNode.NodeType != WorkflowNodeType.Merge)
            {
                return new ParallelExecutionOutcome(false, null, $"Parallel node '{parallelNode.NodeKey}' branch '{branchNode.NodeKey}' must lead directly to a Merge node — branches longer than one node are not supported.");
            }

            if (mergeNode is not null && mergeNode.Id != branchMergeNode.Id)
            {
                return new ParallelExecutionOutcome(false, null, $"Parallel node '{parallelNode.NodeKey}' branches converge at different Merge nodes — every branch must converge at the same one.");
            }

            mergeNode = branchMergeNode;
            branchNodes.Add(branchNode);
        }

        using var mergeConfiguration = JsonDocument.Parse(mergeNode!.ConfigurationJson);
        var strategy = mergeConfiguration.RootElement.TryGetProperty("strategy", out var strategyElement) && strategyElement.ValueKind == JsonValueKind.String
            ? strategyElement.GetString()!
            : "AllCompleted";

        using var semaphore = new SemaphoreSlim(budgetGuard.ResolveMaxParallelNodes(executionPolicy));

        async Task<(WorkflowNode Node, WorkflowNodeExecutionResult Result)> RunBranchAsync(WorkflowNode branchNode)
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var executor = executorRegistry.Find(branchNode.NodeType);
                if (executor is null)
                {
                    return (branchNode, WorkflowNodeExecutionResult.Failure($"No executor is registered for node type '{branchNode.NodeType}'."));
                }

                // Isolated snapshot per branch (research.md Decision 9's "scoped to only the
                // variables/outputs available to it") — a shallow copy is enough since every value
                // here is an immutable WorkflowExpressionValue.
                var branchResolvedValues = new Dictionary<string, WorkflowExpressionValue>(resolvedValues, StringComparer.Ordinal);
                var context = new WorkflowNodeExecutionContext(execution.Id, Guid.CreateVersion7(), execution.RunByUserId, execution.WorkflowId, execution.WorkflowVersionId, branchNode);
                using var input = WorkflowResolvedValues.ToInputDocument(branchResolvedValues);
                var result = await executor.ExecuteAsync(context, input, cancellationToken);
                return (branchNode, result);
            }
            finally
            {
                semaphore.Release();
            }
        }

        List<(WorkflowNode Node, WorkflowNodeExecutionResult Result)> branchResults;
        if (strategy == "FirstCompleted")
        {
            var pending = branchNodes.Select(RunBranchAsync).ToList();
            var settled = new List<(WorkflowNode, WorkflowNodeExecutionResult)>();
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending);
                pending.Remove(finished);
                var outcome = await finished;
                settled.Add(outcome);
                if (outcome.Result.Succeeded)
                {
                    break;
                }
            }

            branchResults = settled;
        }
        else
        {
            branchResults = [.. await Task.WhenAll(branchNodes.Select(RunBranchAsync))];
        }

        var anySucceeded = false;
        // (node, eventType, failureMessage) — the live push waits until every branch's row is
        // persisted in the single SaveChangesAsync below (IWorkflowExecutionNotifier's payload must
        // always mirror an already-persisted row), so notifications are deferred to a second pass.
        var pendingNotifications = new List<(Guid NodeId, WorkflowExecutionEventType EventType, string? FailureMessage)>();
        foreach (var (branchNode, result) in branchResults)
        {
            var executionNode = execution.AddNode(branchNode.Id);
            executionNode.Start(inputJson: null);
            execution.RecordEvent(WorkflowExecutionEventType.NodeStarted, branchNode.Id, "Running", null);
            pendingNotifications.Add((branchNode.Id, WorkflowExecutionEventType.NodeStarted, null));

            if (result.Succeeded)
            {
                anySucceeded = true;
                var outputJson = result.Output?.RootElement.GetRawText();
                executionNode.Complete(outputJson, resolvedIdempotencyKey: null);
                execution.RecordEvent(WorkflowExecutionEventType.NodeCompleted, branchNode.Id, "Completed", null);
                pendingNotifications.Add((branchNode.Id, WorkflowExecutionEventType.NodeCompleted, null));
                WorkflowResolvedValues.AddFlattened(resolvedValues, $"steps.{branchNode.NodeKey}", outputJson);
            }
            else
            {
                execution.RecordError(WorkflowErrorCategory.NodeExecutionFailure, result.FailureReason ?? "The branch did not complete successfully.", executionNode.Id, retryCount: 0);
                executionNode.Fail();
                execution.RecordEvent(WorkflowExecutionEventType.NodeFailed, branchNode.Id, "Failed", null);
                pendingNotifications.Add((branchNode.Id, WorkflowExecutionEventType.NodeFailed, result.FailureReason ?? "The branch did not complete successfully."));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var (nodeId, eventType, failureMessage) in pendingNotifications)
        {
            await (eventType switch
            {
                WorkflowExecutionEventType.NodeStarted => notifier.NotifyNodeStartedAsync(execution.RunByUserId, execution.Id, nodeId, DateTime.UtcNow, cancellationToken),
                WorkflowExecutionEventType.NodeCompleted => notifier.NotifyNodeCompletedAsync(execution.RunByUserId, execution.Id, nodeId, DateTime.UtcNow, cancellationToken),
                _ => notifier.NotifyNodeFailedAsync(execution.RunByUserId, execution.Id, nodeId, failureMessage!, DateTime.UtcNow, cancellationToken),
            });
        }

        var strategyTolerant = strategy is "AnyCompleted" or "FirstCompleted";
        if (!anySucceeded || (!strategyTolerant && branchResults.Any(r => !r.Result.Succeeded)))
        {
            return new ParallelExecutionOutcome(false, null, $"Parallel node '{parallelNode.NodeKey}' branches did not satisfy the '{strategy}' merge strategy.");
        }

        return new ParallelExecutionOutcome(true, mergeNode, null);
    }

    /// <summary>FR-029 — every node reachable from <paramref name="skippedTargetId"/> but NOT also reachable from <paramref name="chosenTargetId"/> (a shared downstream reconvergence point will run via the chosen path regardless, so it is never marked skipped) is recorded <see cref="WorkflowExecutionNodeStatus.Skipped"/>.</summary>
    private static void MarkExclusivelyReachableAsSkipped(
        WorkflowExecution execution, Guid skippedTargetId, Guid chosenTargetId, IReadOnlyList<WorkflowConnection> connections, HashSet<Guid> skippedNodeIds)
    {
        var reachableFromChosen = ComputeReachable(chosenTargetId, connections);
        var reachableFromSkipped = ComputeReachable(skippedTargetId, connections);

        foreach (var nodeId in reachableFromSkipped)
        {
            if (reachableFromChosen.Contains(nodeId) || !skippedNodeIds.Add(nodeId))
            {
                continue;
            }

            execution.AddNode(nodeId).Skip("Skipped — not reached by the workflow's chosen Condition branch.");
        }
    }

    private static HashSet<Guid> ComputeReachable(Guid fromNodeId, IReadOnlyList<WorkflowConnection> connections)
    {
        var visited = new HashSet<Guid> { fromNodeId };
        var queue = new Queue<Guid>();
        queue.Enqueue(fromNodeId);

        while (queue.Count > 0)
        {
            var nodeId = queue.Dequeue();
            foreach (var edge in connections.Where(c => c.SourceNodeId == nodeId && c.BranchLabel != WorkflowConnection.LoopBackBranchLabel))
            {
                if (visited.Add(edge.TargetNodeId))
                {
                    queue.Enqueue(edge.TargetNodeId);
                }
            }
        }

        return visited;
    }

    /// <summary>FR-032 — the loop body's first node conventionally declares its bound as a top-level <c>"maxIterations"</c> integer (the same property <c>WorkflowGraphValidator.ValidateBoundedLoops</c> requires present at publish time).</summary>
    private static int? ExtractMaxIterations(string configurationJson)
    {
        try
        {
            using var configuration = JsonDocument.Parse(configurationJson);
            return configuration.RootElement.TryGetProperty("maxIterations", out var maxIterationsElement) && maxIterationsElement.ValueKind == JsonValueKind.Number
                ? maxIterationsElement.GetInt32()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private WorkflowNodeExecutionResult EvaluateEndNodeOutputs(WorkflowNode node, Dictionary<string, WorkflowExpressionValue> resolvedValues)
    {
        using var configuration = JsonDocument.Parse(node.ConfigurationJson);
        if (!configuration.RootElement.TryGetProperty("outputs", out var outputsElement) || outputsElement.ValueKind != JsonValueKind.Object)
        {
            return WorkflowNodeExecutionResult.Success(JsonDocument.Parse("{}"));
        }

        var outputs = new Dictionary<string, WorkflowExpressionValue>(StringComparer.Ordinal);
        foreach (var property in outputsElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return WorkflowNodeExecutionResult.Failure($"End node output '{property.Name}' must be an expression string.");
            }

            try
            {
                var ast = expressionEvaluator.Parse(property.Value.GetString()!);
                outputs[property.Name] = expressionEvaluator.Evaluate(ast, resolvedValues);
            }
            catch (Exception ex) when (ex is WorkflowExpressionParseException or WorkflowExpressionEvaluationException)
            {
                return WorkflowNodeExecutionResult.Failure($"End node output '{property.Name}' could not be resolved: {ex.Message}");
            }
        }

        return WorkflowNodeExecutionResult.Success(WorkflowResolvedValues.ToInputDocument(outputs));
    }

    private static WorkflowErrorCategory CategorizeFailure(Exception ex) => ex switch
    {
        JsonException => WorkflowErrorCategory.ValidationFailure,
        KeyNotFoundException => WorkflowErrorCategory.NodeExecutionFailure,
        OperationCanceledException => WorkflowErrorCategory.Timeout,
        _ => WorkflowErrorCategory.ProviderFailure,
    };

    /// <summary>Never the raw exception message/stack trace — a short, user-safe summary only (constitution §8/§14 Logging).</summary>
    private static string SafeFailureMessage(Exception ex) => ex switch
    {
        JsonException => "The workflow definition contains invalid data.",
        KeyNotFoundException => "A resource this execution depends on could not be found.",
        InvalidOperationException => ex.Message,
        OperationCanceledException => "The execution exceeded its time limit.",
        _ => "The execution failed unexpectedly — if this persists, try again.",
    };
}
