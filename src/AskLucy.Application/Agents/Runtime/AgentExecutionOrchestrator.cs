using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Ai;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Chats;

namespace AskLucy.Application.Agents.Runtime;

/// <summary>
/// Executes an <see cref="AgentExecution"/>'s plan step-by-step (spec.md FR-011-FR-019,
/// FR-037-FR-041, research.md Decision 8) — pure orchestration logic, callable directly in tests
/// without Hangfire/SignalR (<see cref="IAgentExecutionRunner"/> is the Hangfire-facing entry
/// point).
///
/// <para>Runs inside a background job with no HTTP context, so it never depends on <c>
/// ICurrentUserAccessor</c> — every actor is <see cref="AgentExecution.RunByUserId"/> (known from
/// the execution itself) or <see cref="SystemActor"/>, and conversation writes go directly through
/// <c>IUserChatRepository</c>/<c>IMessageRepository</c> rather than <c>ISender.Send(AppendMessageCommand)</c>,
/// whose handler would otherwise throw <see cref="UnauthorizedAccessException"/> outside a request
/// (mirrors <c>MemoryExtractionJob</c>'s identical constraint).</para>
///
/// <para>Every failure path — a thrown exception from planning, provider calls, tool execution,
/// or a persistence conflict (FR-041; caught here generically as <see cref="Exception"/> rather
/// than the EF Core-specific <c>DbUpdateConcurrencyException</c>, which <c>Application</c> must
/// never reference directly per constitution §3 — <c>ProblemDetailsMiddleware</c> is the
/// HTTP-request equivalent of this same principle, this is its background-job counterpart) — is
/// caught once at the top level, recorded as an <see cref="AgentExecutionError"/> row, and
/// transitions the execution to <see cref="AgentExecutionStatus.Failed"/>; nothing is ever
/// silently swallowed (constitution §2.VIII).</para>
///
/// <para><see cref="RunAsync"/> is resumable (spec.md FR-017, User Story 3): a High/Critical-risk
/// tool call with no matching <see cref="AgentPolicy"/> pauses the execution
/// (<see cref="AgentExecutionStatus.WaitingForApproval"/>) and returns without completing the
/// plan. <c>ApproveAgentActionCommand</c>/<c>RejectAgentActionCommand</c> decide the pending
/// <see cref="AgentApproval"/> and, on approval, re-enqueue this same executionId. A resumed run
/// reuses the already-persisted <see cref="AgentExecution.PlanJson"/> instead of re-planning, and
/// rebuilds in-memory context/citations from steps a prior run already completed, so no step ever
/// runs twice and no progress is lost across the pause.</para>
/// </summary>
public sealed class AgentExecutionOrchestrator(
    IAgentExecutionRepository executionRepository,
    IAgentRepository agentRepository,
    IAIProviderRepository providerRepository,
    IAIModelRepository modelRepository,
    IAIProviderResolver providerResolver,
    IAgentPlanner planner,
    AgentToolCatalog toolCatalog,
    AgentBudgetGuard budgetGuard,
    AgentDuplicateToolCallDetector duplicateDetector,
    AgentPolicyEvaluator policyEvaluator,
    IAgentExecutionNotifier notifier,
    IAgentAuditLogRepository auditLogRepository,
    IUserChatRepository userChatRepository,
    IMessageRepository messageRepository,
    IUnitOfWork unitOfWork)
{
    private const string SystemActor = "system:agent-runtime";
    private const int MaxToolRetryAttempts = 3;

    /// <summary>research.md Decision 12 — the exact permission set that makes a tool call "mutating" for the test-execution skip (SC-007). <see cref="AgentToolPermission.ExternalNetwork"/>/read permissions are deliberately excluded — only these five ever write.</summary>
    private static readonly HashSet<AgentToolPermission> MutatingPermissions =
    [
        AgentToolPermission.WriteFile, AgentToolPermission.ModifyData, AgentToolPermission.SendEmail,
        AgentToolPermission.ExecuteCode, AgentToolPermission.HighRiskOperation,
    ];

    private static bool RequiresMutatingPermission(IAgentTool tool) => tool.RequiredPermissions.Any(MutatingPermissions.Contains);

    public async Task RunAsync(Guid executionId, CancellationToken cancellationToken = default)
    {
        var execution = await executionRepository.GetByIdAsync(executionId, cancellationToken)
            ?? throw new KeyNotFoundException("Agent execution not found.");

        if (execution.Status is AgentExecutionStatus.Completed or AgentExecutionStatus.Failed or AgentExecutionStatus.Cancelled)
        {
            return; // Already terminal — nothing to do (a defensive guard against a duplicate/racing enqueue).
        }

        try
        {
            var agentVersion = await agentRepository.GetVersionByIdAsync(execution.AgentVersionId, cancellationToken)
                ?? throw new InvalidOperationException("The agent version this execution ran under no longer exists.");

            var isFirstRun = execution.StartedAtUtc is null;
            execution.Start();
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (isFirstRun)
            {
                await RecordAndNotifyAsync(
                    execution, agentVersion.Id, AgentExecutionEventType.ExecutionStarted, stepId: null, status: "Running", safeMetadataJson: null,
                    () => notifier.NotifyExecutionStartedAsync(execution.RunByUserId, execution.Id, execution.AgentId, agentVersion.VersionNumber, execution.Objective, DateTime.UtcNow, cancellationToken),
                    cancellationToken);
            }

            var provider = await providerRepository.GetByIdAsync(agentVersion.ModelProviderId, cancellationToken)
                ?? throw new InvalidOperationException("The agent's configured AI provider no longer exists.");
            var model = await modelRepository.GetByIdAsync(agentVersion.ModelId, cancellationToken)
                ?? throw new InvalidOperationException("The agent's configured AI model no longer exists.");
            var aiProvider = providerResolver.Resolve(provider.ProviderKey);

            var availableTools = ResolveAvailableTools(agentVersion.ToolsSnapshotJson);

            AgentPlan plan;
            if (execution.PlanJson is { } existingPlanJson)
            {
                // Resuming after a pause (approval decided, FR-017) — the plan was already created
                // and persisted on the run that paused; re-planning here would discard progress.
                plan = JsonSerializer.Deserialize<AgentPlan>(existingPlanJson)!;
            }
            else
            {
                plan = await planner.CreatePlanAsync(
                    execution.Objective, agentVersion.Instructions, availableTools,
                    provider.ProviderKey, model.ModelKey, model.SupportsJsonMode, cancellationToken);
                execution.SetPlan(JsonSerializer.Serialize(plan));
                await unitOfWork.SaveChangesAsync(cancellationToken);

                await RecordAndNotifyAsync(
                    execution, agentVersion.Id, AgentExecutionEventType.PlanCreated, stepId: null, status: "Created", JsonSerializer.Serialize(new { stepCount = plan.Steps.Count }),
                    () => notifier.NotifyPlanCreatedAsync(execution.RunByUserId, execution.Id, plan.Steps.Count, DateTime.UtcNow, cancellationToken),
                    cancellationToken);
            }

            var priorToolCalls = (await executionRepository.ListToolCallsByStepIdsAsync(
                execution.Steps.Select(s => s.Id).ToList(), cancellationToken)).ToList();

            // Rebuild in-memory state (context/citations/final text) from any steps a prior,
            // paused run of this same execution already completed — a no-op on a fresh execution,
            // since execution.Steps is then empty (FR-017 resume correctness).
            var stepIdByPlanIndex = new Dictionary<int, Guid>();
            var contextEntries = new List<string>();
            var citations = new List<object>();
            string? finalOutputText = null;
            var totalRetryCount = 0;

            foreach (var priorStep in execution.Steps.OrderBy(s => s.StepIndex))
            {
                stepIdByPlanIndex[priorStep.StepIndex] = priorStep.Id;

                if (priorStep.Status != AgentExecutionStepStatus.Completed || priorStep.OutputJson is null)
                {
                    continue;
                }

                if (priorStep.StepType == AgentExecutionStepType.ModelReasoning)
                {
                    var output = JsonDocument.Parse(priorStep.OutputJson).RootElement.GetProperty("output").GetString();
                    finalOutputText = output;
                    contextEntries.Add($"Step \"{priorStep.Description}\" produced: {output}");
                }
                else if (priorStep.StepType == AgentExecutionStepType.ToolCall && priorStep.ToolName is not null)
                {
                    contextEntries.Add(RetrievalPromptFraming.BuildToolResultSystemMessage(priorStep.ToolName, priorStep.OutputJson));

                    if (priorStep.ToolName == "KnowledgeSearchTool")
                    {
                        using var priorOutput = JsonDocument.Parse(priorStep.OutputJson);
                        if (priorOutput.RootElement.TryGetProperty("citations", out var priorCitations))
                        {
                            foreach (var citation in priorCitations.EnumerateArray())
                            {
                                citations.Add(JsonSerializer.Deserialize<object>(citation.GetRawText())!);
                            }
                        }
                    }
                }
            }

            foreach (var planStep in plan.Steps)
            {
                // FR-017/SC-009 — observed at every step boundary, not continuously: a pause/cancel
                // requested from a concurrent HTTP request (PauseAgentExecutionCommand/
                // CancelAgentExecutionCommand) already persisted the correct status itself via its
                // own tracked entity, so this run exits immediately without touching (or re-saving)
                // its own now-stale in-memory `execution` — doing so would either overwrite that
                // status or throw a spurious concurrency conflict.
                var externalStatus = await executionRepository.GetStatusAsync(execution.Id, cancellationToken);
                if (externalStatus is AgentExecutionStatus.Paused or AgentExecutionStatus.Cancelled)
                {
                    return;
                }

                var resumedStep = stepIdByPlanIndex.TryGetValue(planStep.StepIndex, out var existingStepId)
                    ? execution.Steps.First(s => s.Id == existingStepId)
                    : null;

                if (resumedStep is { Status: AgentExecutionStepStatus.Completed or AgentExecutionStepStatus.Skipped })
                {
                    continue; // Already accounted for in the context rebuild above.
                }

                AgentExecutionStep step;
                if (resumedStep is null)
                {
                    var budgetCheck = budgetGuard.Check(
                        agentVersion.ExecutionPolicy, execution.StartedAtUtc!.Value, execution.Steps.Count + 1,
                        priorToolCalls.Count, totalRetryCount, execution.Usage?.InputTokenCount, execution.Usage?.OutputTokenCount, execution.Cost?.EstimatedCost);
                    if (budgetCheck.IsExceeded)
                    {
                        execution.RecordError(AgentExecutionErrorCategory.BudgetExceeded, budgetCheck.Reason!, stepId: null, retryCount: 0);
                        execution.Fail(budgetCheck.Reason!);
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        return;
                    }

                    Guid? dependsOnStepId = planStep.DependsOnStepIndex is { } depIdx && stepIdByPlanIndex.TryGetValue(depIdx, out var depId) ? depId : null;
                    step = execution.AddStep(planStep.StepIndex, planStep.Description, planStep.StepType, dependsOnStepId, planStep.ToolName, inputJson: null);
                    stepIdByPlanIndex[planStep.StepIndex] = step.Id;
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    step = resumedStep;
                }

                step.Start();
                await unitOfWork.SaveChangesAsync(cancellationToken);
                await RecordAndNotifyAsync(
                    execution, agentVersion.Id, AgentExecutionEventType.StepStarted, step.Id, "Running", null,
                    () => notifier.NotifyStepStartedAsync(execution.RunByUserId, execution.Id, step.Id, step.StepIndex, step.Description, DateTime.UtcNow, cancellationToken),
                    cancellationToken);

                if (planStep.StepType == AgentExecutionStepType.ModelReasoning)
                {
                    var messages = BuildReasoningMessages(agentVersion.Instructions, execution.Objective, contextEntries);
                    var result = await aiProvider.ChatAsync(messages, model.ModelKey, parameters: null, cancellationToken);

                    finalOutputText = result.Content;
                    step.Complete(JsonSerializer.Serialize(new { output = result.Content }));
                    contextEntries.Add($"Step \"{planStep.Description}\" produced: {result.Content}");
                    AccumulateUsage(execution, result.Usage);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    await RecordAndNotifyAsync(
                        execution, agentVersion.Id, AgentExecutionEventType.StepCompleted, step.Id, "Completed", null,
                        () => notifier.NotifyStepCompletedAsync(execution.RunByUserId, execution.Id, step.Id, step.StepIndex, step.Description, DateTime.UtcNow, cancellationToken),
                        cancellationToken);
                    await NotifyUsageUpdatedAsync(execution, cancellationToken);
                }
                else
                {
                    var tool = availableTools.First(t => t.Name == planStep.ToolName);

                    if (resumedStep is null && execution.IsTestExecution && RequiresMutatingPermission(tool))
                    {
                        // research.md Decision 12/SC-007 — a test execution never invokes a
                        // mutating tool at all (not even to create a pending approval candidate);
                        // "zero unintended changes to production data" is guaranteed by never
                        // calling ExecuteAsync, not by relying on nobody approving the result.
                        step.Skip("write actions are disabled for test executions");
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        await RecordAndNotifyAsync(
                            execution, agentVersion.Id, AgentExecutionEventType.StepCompleted, step.Id, "Skipped", null,
                            () => notifier.NotifyStepCompletedAsync(execution.RunByUserId, execution.Id, step.Id, step.StepIndex, step.Description, DateTime.UtcNow, cancellationToken),
                            cancellationToken);
                        continue;
                    }

                    var isResumingThisToolStep = resumedStep is not null;

                    AgentToolCall toolCall;
                    string inputJson;

                    if (isResumingThisToolStep)
                    {
                        // The approval gate below already created and persisted this tool call
                        // (with its validated input) on the run that paused — FR-025/FR-017.
                        toolCall = priorToolCalls.First(tc => tc.AgentExecutionStepId == step.Id);
                        inputJson = toolCall.ValidatedInputJson;
                    }
                    else
                    {
                        inputJson = await ResolveToolInputAsync(aiProvider, model.ModelKey, model.SupportsJsonMode, execution.Objective, planStep, tool, contextEntries, cancellationToken);

                        if (duplicateDetector.IsDuplicate(priorToolCalls, tool.Name, inputJson))
                        {
                            execution.RecordError(AgentExecutionErrorCategory.ToolFailure, $"Detected a repeated identical call to '{tool.Name}' — halting to prevent an infinite loop.", step.Id, 0);
                            execution.Fail("Duplicate tool call detected.");
                            await unitOfWork.SaveChangesAsync(cancellationToken);
                            return;
                        }

                        var requiresApproval = tool.RiskLevel is AgentToolRiskLevel.High or AgentToolRiskLevel.Critical;
                        toolCall = AgentToolCall.Create(
                            step.Id, tool.Name, tool.RiskLevel, JsonSerializer.Serialize(tool.RequiredPermissions), inputJson, requiresApproval);
                        executionRepository.AddToolCall(toolCall);
                        priorToolCalls.Add(toolCall);
                        await unitOfWork.SaveChangesAsync(cancellationToken);

                        if (requiresApproval)
                        {
                            // FR-025/FR-026 — a matching administrator policy lets the call proceed
                            // without an interactive prompt; every decision (interactive or
                            // policy-based) is still recorded (FR-028).
                            var matchedPolicy = await policyEvaluator.FindMatchAsync(tool.Name, inputJson, cancellationToken);
                            var approval = execution.RequestApproval(toolCall.Id, $"Execute {tool.Name}", inputJson);

                            if (matchedPolicy is null)
                            {
                                step.WaitForApproval();
                                await unitOfWork.SaveChangesAsync(cancellationToken);
                                await RecordAndNotifyAsync(
                                    execution, agentVersion.Id, AgentExecutionEventType.ApprovalRequested, step.Id, "WaitingForApproval", null,
                                    () => notifier.NotifyApprovalRequestedAsync(execution.RunByUserId, execution.Id, approval.Id, approval.IntendedActionDescription, tool.RiskLevel, DateTime.UtcNow, cancellationToken),
                                    cancellationToken);
                                return; // Pauses indefinitely — consumes no further budget until decided (FR-025, Edge Cases).
                            }

                            approval.ApproveByPolicy(matchedPolicy.Id);
                            execution.Resume();
                            await unitOfWork.SaveChangesAsync(cancellationToken);
                            await RecordAndNotifyAsync(
                                execution, agentVersion.Id, AgentExecutionEventType.ApprovalGranted, step.Id, "Approved", JsonSerializer.Serialize(new { wasPolicyBased = true }),
                                () => notifier.NotifyApprovalGrantedAsync(execution.RunByUserId, execution.Id, approval.Id, decidedByUserId: null, wasPolicyBased: true, DateTime.UtcNow, cancellationToken),
                                cancellationToken);
                        }
                    }

                    await RecordAndNotifyAsync(
                        execution, agentVersion.Id, AgentExecutionEventType.ToolCallStarted, step.Id, "Running", null,
                        () => notifier.NotifyToolCallStartedAsync(execution.RunByUserId, execution.Id, step.Id, tool.Name, tool.RiskLevel, DateTime.UtcNow, cancellationToken),
                        cancellationToken);

                    var toolContext = new AgentToolExecutionContext(execution.Id, step.Id, execution.RunByUserId, execution.AgentId, execution.AgentVersionId, execution.UserChatId);
                    var (toolResult, retriesUsed) = await ExecuteToolWithRetryAsync(tool, toolContext, inputJson, cancellationToken);
                    totalRetryCount += retriesUsed;

                    if (!toolResult.Succeeded)
                    {
                        var error = execution.RecordError(AgentExecutionErrorCategory.ToolFailure, toolResult.FailureReason ?? "The tool did not complete successfully.", step.Id, retriesUsed);
                        toolCall.Fail(toolResult.FailureReason ?? "Tool execution failed.");
                        step.Fail(error.Id);
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        await RecordAndNotifyAsync(
                            execution, agentVersion.Id, AgentExecutionEventType.ToolCallCompleted, step.Id, "Failed", null,
                            () => notifier.NotifyToolCallCompletedAsync(execution.RunByUserId, execution.Id, step.Id, tool.Name, tool.RiskLevel, succeeded: false, DateTime.UtcNow, cancellationToken),
                            cancellationToken);
                        await RecordAndNotifyAsync(
                            execution, agentVersion.Id, AgentExecutionEventType.StepFailed, step.Id, "Failed", null,
                            () => notifier.NotifyStepFailedAsync(execution.RunByUserId, execution.Id, step.Id, step.StepIndex, step.Description, toolResult.FailureReason ?? "Tool execution failed.", DateTime.UtcNow, cancellationToken),
                            cancellationToken);
                    }
                    else
                    {
                        var outputJson = toolResult.Output!.RootElement.GetRawText();
                        toolCall.Complete(outputJson);
                        step.Complete(outputJson);
                        contextEntries.Add(RetrievalPromptFraming.BuildToolResultSystemMessage(tool.Name, outputJson));

                        if (tool.Name == "KnowledgeSearchTool" && toolResult.Output.RootElement.TryGetProperty("citations", out var citationsElement))
                        {
                            foreach (var citation in citationsElement.EnumerateArray())
                            {
                                citations.Add(JsonSerializer.Deserialize<object>(citation.GetRawText())!);
                            }
                        }

                        await unitOfWork.SaveChangesAsync(cancellationToken);
                        await RecordAndNotifyAsync(
                            execution, agentVersion.Id, AgentExecutionEventType.ToolCallCompleted, step.Id, "Completed", null,
                            () => notifier.NotifyToolCallCompletedAsync(execution.RunByUserId, execution.Id, step.Id, tool.Name, tool.RiskLevel, succeeded: true, DateTime.UtcNow, cancellationToken),
                            cancellationToken);
                        await RecordAndNotifyAsync(
                            execution, agentVersion.Id, AgentExecutionEventType.StepCompleted, step.Id, "Completed", null,
                            () => notifier.NotifyStepCompletedAsync(execution.RunByUserId, execution.Id, step.Id, step.StepIndex, step.Description, DateTime.UtcNow, cancellationToken),
                            cancellationToken);
                    }
                }
            }

            if (execution.ConversationIntegrationMode != AgentConversationIntegrationMode.Standalone)
            {
                await PostResultToConversationAsync(execution, finalOutputText, cancellationToken);
            }

            AccumulateCost(execution, model.Pricing);

            var finalOutputJson = citations.Count > 0 ? JsonSerializer.Serialize(new { citations }) : null;
            execution.Complete(finalOutputText, finalOutputJson);
            auditLogRepository.Add(AgentAuditLog.Create(execution.Id, execution.RunByUserId, AgentAuditAction.ExecutionCompleted, "{}"));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await RecordAndNotifyAsync(
                execution, agentVersion.Id, AgentExecutionEventType.ExecutionCompleted, stepId: null, status: "Completed", safeMetadataJson: null,
                () => notifier.NotifyExecutionCompletedAsync(execution.RunByUserId, execution.Id, DateTime.UtcNow, cancellationToken),
                cancellationToken);
            await NotifyUsageUpdatedAsync(execution, cancellationToken);
        }
        catch (Exception ex)
        {
            execution.RecordError(CategorizeFailure(ex), SafeFailureMessage(ex), stepId: null, retryCount: 0);
            execution.Fail(SafeFailureMessage(ex));
            auditLogRepository.Add(AgentAuditLog.Create(execution.Id, execution.RunByUserId, AgentAuditAction.ExecutionFailed, "{}"));
            if (ex is KeyNotFoundException)
            {
                // A tool's own ownership guard (e.g. DocumentOwnershipGuard inside FileReadTool)
                // threw — the execution never held a permission it needed (FR-022, Edge Cases:
                // "the execution fails that step with a clear permission/availability error").
                auditLogRepository.Add(AgentAuditLog.Create(execution.Id, execution.RunByUserId, AgentAuditAction.PermissionDenied, "{}"));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await RecordAndNotifyAsync(
                execution, execution.AgentVersionId, AgentExecutionEventType.ExecutionFailed, stepId: null, status: "Failed", safeMetadataJson: null,
                () => notifier.NotifyExecutionFailedAsync(execution.RunByUserId, execution.Id, execution.TerminationReason ?? "The execution failed.", DateTime.UtcNow, cancellationToken),
                cancellationToken);
        }
    }

    /// <summary>FR-034/contracts/agent-execution-events.md — persists an <see cref="AgentExecutionEvent"/> row (append-only, safe-metadata-only per FR-035), then pushes the corresponding live payload over <see cref="IAgentExecutionNotifier"/>. The persisted row is always the reconciliation source of truth if the live push is missed.</summary>
    private async Task RecordAndNotifyAsync(
        AgentExecution execution, Guid agentVersionId, AgentExecutionEventType eventType, Guid? stepId, string status, string? safeMetadataJson,
        Func<Task> notify, CancellationToken cancellationToken)
    {
        execution.RecordEvent(eventType, agentVersionId, stepId, status, safeMetadataJson);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await notify();
    }

    /// <summary>FR-036 — pushed opportunistically at natural checkpoints (a model call completing, execution completion), never per-token, satisfying SC-002's 2s visibility without a token-by-token firehose.</summary>
    private Task NotifyUsageUpdatedAsync(AgentExecution execution, CancellationToken cancellationToken) =>
        notifier.NotifyUsageUpdatedAsync(execution.RunByUserId, execution.Id, execution.Usage?.InputTokenCount, execution.Usage?.OutputTokenCount, execution.Cost?.EstimatedCost, cancellationToken);

    /// <summary>Resolves the agent's configured tools (data-model.md <c>AgentVersion.ToolsSnapshotJson</c>, written by <c>Agent.Publish</c>) against the compile-time catalog (research.md Decision 10). A configured tool no longer in the catalog is silently skipped, not a failure — matches FR-049's "never broader than authorized" spirit for a removed capability.</summary>
    private List<IAgentTool> ResolveAvailableTools(string toolsSnapshotJson)
    {
        using var document = JsonDocument.Parse(toolsSnapshotJson);
        var tools = new List<IAgentTool>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("ToolName", out var nameElement) &&
                nameElement.GetString() is { } toolName &&
                toolCatalog.Find(toolName) is { } tool)
            {
                tools.Add(tool);
            }
        }

        return tools;
    }

    private static List<ChatMessage> BuildReasoningMessages(AgentInstructions instructions, string objective, IReadOnlyList<string> contextEntries)
    {
        var messages = new List<ChatMessage> { new(ChatRole.System, instructions.SystemInstructions ?? string.Empty) };
        messages.AddRange(contextEntries.Select(entry => new ChatMessage(ChatRole.System, entry)));
        messages.Add(new ChatMessage(ChatRole.User, objective));
        return messages;
    }

    /// <summary>
    /// One structured AI call per tool-call step to derive that specific call's input (matching
    /// the tool's declared <see cref="IAgentTool.InputSchemaJson"/>) — the upfront plan only
    /// selects *which* tool a step uses (research.md Decision 11), since a step's specific
    /// arguments often depend on an earlier step's output, which doesn't exist yet when the whole
    /// plan is first created.
    /// </summary>
    private static async Task<string> ResolveToolInputAsync(
        IAIProvider aiProvider, string modelKey, bool modelSupportsJsonMode, string objective,
        AgentPlanStep planStep, IAgentTool tool, IReadOnlyList<string> contextEntries, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, $"Produce ONLY a JSON object matching this schema for the tool \"{tool.Name}\": {tool.InputSchemaJson}. No other text."),
        };
        messages.AddRange(contextEntries.Select(entry => new ChatMessage(ChatRole.System, entry)));
        messages.Add(new ChatMessage(ChatRole.User, $"Objective: {objective}\nCurrent step: {planStep.Description}"));

        var parameters = new GenerationParametersDto(JsonMode: modelSupportsJsonMode ? true : null);
        var result = await aiProvider.ChatAsync(messages, modelKey, parameters, cancellationToken);

        var start = result.Content.IndexOf('{');
        var end = result.Content.LastIndexOf('}');
        return start >= 0 && end > start ? result.Content[start..(end + 1)] : "{}";
    }

    /// <summary>FR-037/FR-038 — retries a tool call with exponential backoff up to <see cref="MaxToolRetryAttempts"/> before giving up.</summary>
    private static async Task<(AgentToolResult Result, int RetriesUsed)> ExecuteToolWithRetryAsync(
        IAgentTool tool, AgentToolExecutionContext context, string inputJson, CancellationToken cancellationToken)
    {
        AgentToolResult? lastResult = null;
        for (var attempt = 0; attempt < MaxToolRetryAttempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
            }

            using var input = JsonDocument.Parse(inputJson);
            lastResult = await tool.ExecuteAsync(context, input, cancellationToken);
            if (lastResult.Succeeded)
            {
                return (lastResult, attempt);
            }
        }

        return (lastResult!, MaxToolRetryAttempts - 1);
    }

    /// <summary>FR-036/User Story 4 usage visibility — accumulated per model call; persisted cost derives from it via <c>AIModel.Pricing</c> at completion.</summary>
    private static void AccumulateUsage(AgentExecution execution, ChatUsage usage)
    {
        var current = execution.Usage ?? AgentExecutionUsage.CreateEmpty(execution.Id);
        current.Accumulate(usage.InputTokenCount, usage.OutputTokenCount, usage.ReasoningTokenCount, additionalToolCalls: 0, additionalSteps: 1);
        execution.SetUsage(current);
    }

    /// <summary>FR-036 — derives <see cref="AgentExecutionCost"/> from accumulated <see cref="AgentExecutionUsage"/> via the model's own <c>ModelPricing</c> (no new pricing logic, matching every other AI-cost calculation in the codebase). A model with no configured pricing yields a zero estimate rather than a failure — cost visibility degrades gracefully, it never blocks completion.</summary>
    private static void AccumulateCost(AgentExecution execution, ModelPricing? pricing)
    {
        if (execution.Usage is not { } usage || pricing is null)
        {
            return;
        }

        var inputCost = (usage.InputTokenCount ?? 0) / 1_000_000m * pricing.InputPerMillionTokensUsd;
        var outputCost = (usage.OutputTokenCount ?? 0) / 1_000_000m * pricing.OutputPerMillionTokensUsd;

        execution.SetCost(AgentExecutionCost.Create(execution.Id, inputCost + outputCost, "USD"));
    }

    /// <summary>FR-051/FR-052 — posts the objective + final result as a user/assistant turn into the linked conversation. Direct repository access, not <c>ISender.Send(AppendMessageCommand)</c> — see this class's doc comment.</summary>
    private async Task PostResultToConversationAsync(AgentExecution execution, string? finalOutputText, CancellationToken cancellationToken)
    {
        if (execution.UserChatId is not { } userChatId)
        {
            return;
        }

        var chat = await userChatRepository.GetByIdAsync(userChatId, cancellationToken);
        if (chat is null)
        {
            return; // The conversation was deleted mid-execution — nothing to post into.
        }

        var userMessage = Message.Create(userChatId, MessageRole.User, MessageKind.Text, execution.Objective, sourceText: null, execution.RunByUserId);
        messageRepository.Add(userMessage);

        var assistantMessage = Message.Create(userChatId, MessageRole.Assistant, MessageKind.Text, finalOutputText ?? string.Empty, sourceText: null, SystemActor);
        messageRepository.Add(assistantMessage);

        chat.TouchLastActivity(SystemActor);
    }

    private static AgentExecutionErrorCategory CategorizeFailure(Exception ex) => ex switch
    {
        JsonException => AgentExecutionErrorCategory.InvalidModelResponse,
        KeyNotFoundException => AgentExecutionErrorCategory.ToolFailure,
        OperationCanceledException => AgentExecutionErrorCategory.ExecutionTimeout,
        _ => AgentExecutionErrorCategory.ProviderFailure,
    };

    /// <summary>Never the raw exception message/stack trace — a short, user-safe summary only (constitution §8/§21 Logging). A persistence conflict (FR-041) falls into the generic branch — actionable enough ("try again") without naming the EF Core exception type Application must not reference.</summary>
    private static string SafeFailureMessage(Exception ex) => ex switch
    {
        JsonException => "The model did not return a valid response.",
        KeyNotFoundException => "A resource this execution depends on could not be found.",
        InvalidOperationException => ex.Message,
        OperationCanceledException => "The execution exceeded its time limit.",
        _ => "The execution failed unexpectedly — if this persists, try again.",
    };
}
