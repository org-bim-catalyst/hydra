using AskLucy.Application.Abstractions;
using AskLucy.Application.Common;
using AskLucy.Domain.Agents;

namespace AskLucy.Application.Agents.Runtime;

/// <summary>
/// Matches an intended tool call against the enabled <see cref="AgentPolicy"/> rows for that tool
/// (spec.md FR-025/FR-026, data-model.md). <see cref="AgentPolicy.ConditionsJson"/> is a flat JSON
/// object of parameter name/value constraints, evaluated by the shared <see cref="PolicyConditionMatcher"/>
/// (research.md Decision 5, specs/022-workflow-orchestration-engine) — every declared condition
/// must equal the tool call's actual input for the policy to match; a null/empty/whitespace
/// <c>ConditionsJson</c> matches unconditionally ("empty means always", data-model.md).
/// </summary>
public sealed class AgentPolicyEvaluator(IAgentPolicyRepository policyRepository)
{
    public async Task<AgentPolicy?> FindMatchAsync(string toolName, string inputJson, CancellationToken cancellationToken = default)
    {
        var policies = await policyRepository.ListEnabledByToolNameAsync(toolName, cancellationToken);
        return policies.FirstOrDefault(policy => PolicyConditionMatcher.Matches(policy.ConditionsJson, inputJson));
    }
}
