using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>Matches an intended node action against enabled <see cref="WorkflowPolicy"/> rows (FR-035/FR-036, research.md Decision 5) — mirrors <c>AgentPolicyEvaluator</c> exactly.</summary>
public sealed class WorkflowPolicyEvaluator(IWorkflowPolicyRepository policyRepository)
{
    public async Task<WorkflowPolicy?> FindMatchAsync(WorkflowNodeType nodeType, string? underlyingToolName, string inputJson, CancellationToken cancellationToken = default)
    {
        var policies = await policyRepository.ListEnabledForNodeAsync(nodeType, underlyingToolName, cancellationToken);
        return policies.FirstOrDefault(policy => PolicyConditionMatcher.Matches(policy.ConditionsJson, inputJson));
    }
}
