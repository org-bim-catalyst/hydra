using System.Text.Json;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Deserializes <see cref="WorkflowVersion.ExecutionPolicyJson"/> — unlike <c>AgentVersion.ExecutionPolicy</c>
/// (a structured EF-owned value object), the Workflow version stores this as a plain JSON string
/// (research.md Decision 19's "one mutable draft blob" design extended to the immutable version's
/// own policy fields), so callers must parse it themselves before handing it to
/// <see cref="WorkflowBudgetGuard"/>.
/// </summary>
public static class WorkflowExecutionPolicyParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Malformed or empty JSON falls back to <see cref="WorkflowExecutionPolicy.Empty"/> (every limit defaults from <c>WorkflowRuntimeOptions</c>) rather than throwing — a policy is optional by design.</summary>
    public static WorkflowExecutionPolicy Parse(string? executionPolicyJson)
    {
        if (string.IsNullOrWhiteSpace(executionPolicyJson) || executionPolicyJson == "{}")
        {
            return WorkflowExecutionPolicy.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<WorkflowExecutionPolicy>(executionPolicyJson, Options) ?? WorkflowExecutionPolicy.Empty;
        }
        catch (JsonException)
        {
            return WorkflowExecutionPolicy.Empty;
        }
    }
}
