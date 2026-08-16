using System.Text.Json;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>Deserializes <see cref="WorkflowNode.RetryPolicyJson"/> (FR-040), mirroring <see cref="WorkflowExecutionPolicyParser"/>'s tolerant-parse pattern.</summary>
public static class WorkflowNodeRetryPolicyParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Malformed, missing, or empty JSON falls back to <see cref="WorkflowRetryPolicy.Empty"/> (every field null, resolved to a no-retry default by the orchestrator) rather than throwing — a node's retry policy is optional by design.</summary>
    public static WorkflowRetryPolicy Parse(string? retryPolicyJson)
    {
        if (string.IsNullOrWhiteSpace(retryPolicyJson) || retryPolicyJson == "{}")
        {
            return WorkflowRetryPolicy.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<WorkflowRetryPolicy>(retryPolicyJson, Options) ?? WorkflowRetryPolicy.Empty;
        }
        catch (JsonException)
        {
            return WorkflowRetryPolicy.Empty;
        }
    }
}
