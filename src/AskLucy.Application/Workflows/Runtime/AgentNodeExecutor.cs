using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Agents.Runtime;
using AskLucy.Application.Options;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Agents;
using AskLucy.Domain.Workflows;
using Microsoft.Extensions.Options;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Invokes <see cref="AgentExecutionOrchestrator.RunAsync"/> in-process, treating the Agent as an
/// opaque external execution component — never a re-implementation of agent planning (research.md
/// Decision 3, contracts/workflow-node-contract.md). Runs synchronously (not via Hangfire) so this
/// node's result is available to the workflow's own node loop the same way any other executor's is.
///
/// <para><b>Known limitation</b>: if the invoked agent pauses for a required tool-call approval
/// (<see cref="AgentExecutionStatus.WaitingForApproval"/>), this executor fails the node rather
/// than propagating a nested pause up through the workflow execution — the workflow orchestrator
/// does not yet support suspending on a nested agent's approval. Configure the invoked agent's
/// execution policy/tools so it never requires approval, or run it outside a workflow, until
/// nested-approval propagation is implemented.</para>
///
/// Configuration shape: <c>{"agentId": "...", "versionNumber": (optional), "objective": "literal
/// or {{...}}"}</c>.
/// </summary>
public sealed class AgentNodeExecutor(
    IAgentRepository agentRepository,
    IAgentExecutionRepository agentExecutionRepository,
    IAgentPolicyRepository agentPolicyRepository,
    IAgentAuditLogRepository agentAuditLogRepository,
    AgentExecutionOrchestrator agentExecutionOrchestrator,
    IOptions<AgentRuntimeOptions> agentRuntimeOptions,
    IWorkflowExpressionEvaluator expressionEvaluator,
    IUnitOfWork unitOfWork) : IWorkflowNodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.AiAgent;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        var root = configuration.RootElement;

        if (!root.TryGetProperty("agentId", out var agentIdElement) || agentIdElement.ValueKind != JsonValueKind.String || !Guid.TryParse(agentIdElement.GetString(), out var agentId))
        {
            return WorkflowNodeExecutionResult.Failure("AI Agent node configuration requires an 'agentId'.");
        }

        if (!root.TryGetProperty("objective", out var objectiveElement) || objectiveElement.ValueKind != JsonValueKind.String)
        {
            return WorkflowNodeExecutionResult.Failure("AI Agent node configuration requires an 'objective' string.");
        }

        var resolvedValues = WorkflowResolvedValues.ParseInputDocument(input);
        if (!WorkflowCapabilityToolInvoker.TryResolveConfigString(objectiveElement.GetString(), expressionEvaluator, resolvedValues, out var objective, out var expressionError))
        {
            return WorkflowNodeExecutionResult.Failure(expressionError!);
        }

        var agent = await agentRepository.GetByIdForOwnerAsync(agentId, context.UserId, cancellationToken);
        if (agent is null)
        {
            return WorkflowNodeExecutionResult.Failure("The configured agent was not found, or is not owned by the workflow's initiating user.");
        }

        // FR-042/FR-043, mirrors StartAgentExecutionCommandHandler exactly — checked before any
        // side effect, so a rejected nested execution never leaves a half-created row behind.
        var userLimit = await agentPolicyRepository.GetUserExecutionLimitAsync(context.UserId, cancellationToken);
        var maxConcurrentExecutions = userLimit?.MaxConcurrentExecutions ?? agentRuntimeOptions.Value.DefaultMaxConcurrentExecutions;
        var activeCount = await agentExecutionRepository.CountActiveByUserAsync(context.UserId, cancellationToken);
        if (activeCount >= maxConcurrentExecutions)
        {
            return WorkflowNodeExecutionResult.Failure(
                $"You already have {activeCount} agent execution(s) in progress, which is your current limit. Try this workflow again once one finishes.");
        }

        var versionNumber = root.TryGetProperty("versionNumber", out var versionElement) && versionElement.ValueKind == JsonValueKind.Number
            ? versionElement.GetInt32()
            : agent.PublishedVersionNumber;
        if (versionNumber is null)
        {
            return WorkflowNodeExecutionResult.Failure("The configured agent has no published version yet.");
        }

        var agentVersion = await agentRepository.GetVersionAsync(agent.Id, versionNumber.Value, cancellationToken);
        if (agentVersion is null)
        {
            return WorkflowNodeExecutionResult.Failure("The configured agent version was not found.");
        }

        var agentExecution = AgentExecution.Create(
            agent.Id, agentVersion.Id, context.UserId, objective ?? string.Empty, isTestExecution: false,
            AgentConversationIntegrationMode.Standalone, userChatId: null, context.UserId);

        agentExecutionRepository.Add(agentExecution);
        agentAuditLogRepository.Add(AgentAuditLog.Create(agentExecution.Id, context.UserId, AgentAuditAction.PermissionChecked, "{}"));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await agentExecutionOrchestrator.RunAsync(agentExecution.Id, cancellationToken);

        var completed = await agentExecutionRepository.GetByIdAsync(agentExecution.Id, cancellationToken)
            ?? throw new InvalidOperationException("The just-created agent execution disappeared during its own run.");

        return completed.Status switch
        {
            AgentExecutionStatus.Completed => WorkflowNodeExecutionResult.Success(
                WorkflowResolvedValues.ToInputDocument(new Dictionary<string, WorkflowExpressionValue>
                {
                    ["text"] = WorkflowExpressionValue.OfString(completed.FinalOutputText ?? string.Empty),
                })),
            AgentExecutionStatus.WaitingForApproval => WorkflowNodeExecutionResult.Failure(
                "The invoked agent paused for a required approval. Nested approval pauses inside an AI Agent workflow node aren't supported yet — configure this agent's execution policy/tools so it never requires approval, or invoke it outside this workflow."),
            _ => WorkflowNodeExecutionResult.Failure(completed.TerminationReason ?? $"The invoked agent ended with status '{completed.Status}'."),
        };
    }
}
