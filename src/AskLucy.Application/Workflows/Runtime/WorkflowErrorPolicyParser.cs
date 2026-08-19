using System.Text.Json;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>Deserializes <see cref="WorkflowVersion.ErrorPolicyJson"/>, mirroring <see cref="WorkflowExecutionPolicyParser"/> exactly.</summary>
public static class WorkflowErrorPolicyParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Malformed or empty JSON falls back to <see cref="WorkflowErrorPolicy.Empty"/> (strategy defaults to <c>Stop</c>) rather than throwing.</summary>
    public static WorkflowErrorPolicy Parse(string? errorPolicyJson)
    {
        if (string.IsNullOrWhiteSpace(errorPolicyJson) || errorPolicyJson == "{}")
        {
            return WorkflowErrorPolicy.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<WorkflowErrorPolicy>(errorPolicyJson, Options) ?? WorkflowErrorPolicy.Empty;
        }
        catch (JsonException)
        {
            return WorkflowErrorPolicy.Empty;
        }
    }
}
