using System.Text.Json;

namespace AskLucy.Application.Common;

/// <summary>
/// Matches an intended action's input against a policy's flat JSON condition object — shared by
/// <c>AgentPolicyEvaluator</c> and <c>WorkflowPolicyEvaluator</c> (research.md Decision 5,
/// specs/022-workflow-orchestration-engine). Extracted from <c>AgentPolicyEvaluator.Matches</c>'s
/// original private implementation rather than duplicated, since the two evaluators need the
/// exact same rule (constitution §2.III DRY): every declared condition must equal the actual
/// input for the policy to match; a null/empty/whitespace <paramref name="conditionsJson"/>
/// matches unconditionally ("empty means always").
/// </summary>
public static class PolicyConditionMatcher
{
    public static bool Matches(string? conditionsJson, string inputJson)
    {
        if (string.IsNullOrWhiteSpace(conditionsJson))
        {
            return true;
        }

        using var conditions = JsonDocument.Parse(conditionsJson);
        using var input = JsonDocument.Parse(inputJson);

        foreach (var condition in conditions.RootElement.EnumerateObject())
        {
            if (!input.RootElement.TryGetProperty(condition.Name, out var actualValue) ||
                condition.Value.GetRawText() != actualValue.GetRawText())
            {
                return false;
            }
        }

        return true;
    }
}
