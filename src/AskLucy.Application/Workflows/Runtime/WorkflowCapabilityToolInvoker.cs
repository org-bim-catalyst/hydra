using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Workflows.Expressions;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Shared plumbing for every thin-adapter executor that delegates to an existing
/// <see cref="IAgentTool"/> (research.md Decision 1, contracts/workflow-node-contract.md).
/// <see cref="BuildContext"/>'s <c>AgentId</c>/<c>AgentVersionId</c> are set to <see
/// cref="Guid.Empty"/> — verified against every tool this feature currently wraps
/// (KnowledgeSearchTool/MemorySearchTool/DocumentSearchTool/FileReadTool/FileMetadataTool/
/// ConversationTool/McpToolAdapter) that none read them except <c>McpToolAdapter</c>'s own
/// rate-limit key, where <see cref="Guid.Empty"/> simply gives every workflow-triggered call to a
/// given MCP tool by a given user its own rate-limit bucket, separate from that same user's
/// agent-triggered calls to the same tool.
/// </summary>
internal static class WorkflowCapabilityToolInvoker
{
    public static AgentToolExecutionContext BuildContext(WorkflowNodeExecutionContext context) =>
        new(context.WorkflowExecutionId, context.WorkflowExecutionNodeId, context.UserId, AgentId: Guid.Empty, AgentVersionId: Guid.Empty, UserChatId: null);

    public static async Task<WorkflowNodeExecutionResult> InvokeAsync(IAgentTool tool, WorkflowNodeExecutionContext context, JsonDocument toolInput, CancellationToken cancellationToken)
    {
        var result = await tool.ExecuteAsync(BuildContext(context), toolInput, cancellationToken);
        return result.Succeeded
            ? WorkflowNodeExecutionResult.Success(result.Output!)
            : WorkflowNodeExecutionResult.Failure(result.FailureReason ?? $"{tool.Name} execution failed.");
    }

    /// <summary>
    /// Resolves one node-configuration string field (FR-025): a value beginning with
    /// <c>"{{"</c> is evaluated as a workflow expression against the current resolved-values
    /// snapshot; any other value is used literally. Returns <see langword="false"/> with a
    /// caller-displayable <paramref name="error"/> rather than throwing, matching
    /// <c>TransformNodeExecutor</c>'s own parse/evaluate failure handling.
    /// </summary>
    public static bool TryResolveConfigString(
        string? raw,
        IWorkflowExpressionEvaluator evaluator,
        IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues,
        out string? resolved,
        out string? error)
    {
        if (string.IsNullOrEmpty(raw) || !raw.StartsWith("{{", StringComparison.Ordinal))
        {
            resolved = raw;
            error = null;
            return true;
        }

        try
        {
            var ast = evaluator.Parse(raw);
            resolved = evaluator.Evaluate(ast, resolvedValues).ToDisplayString();
            error = null;
            return true;
        }
        catch (WorkflowExpressionParseException ex)
        {
            resolved = null;
            error = $"Invalid expression: {ex.Message}";
            return false;
        }
        catch (WorkflowExpressionEvaluationException ex)
        {
            resolved = null;
            error = $"Expression resolution failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Resolves every top-level string property of a node-configuration object the same way
    /// <see cref="TryResolveConfigString"/> resolves one field — used by <c>McpToolNodeExecutor</c>/
    /// <c>NativeToolNodeExecutor</c> to pass a target tool's own free-form <c>input</c> object
    /// through, substituting any <c>{{...}}</c>-valued fields. Non-string properties (numbers,
    /// booleans, arrays, nested objects) are copied through unchanged.
    /// </summary>
    public static bool TryResolveConfigObject(
        JsonElement configObject,
        IWorkflowExpressionEvaluator evaluator,
        IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues,
        out JsonDocument? resolved,
        out string? error)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in configObject.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    if (!TryResolveConfigString(property.Value.GetString(), evaluator, resolvedValues, out var value, out error))
                    {
                        resolved = null;
                        return false;
                    }

                    writer.WriteString(property.Name, value ?? string.Empty);
                }
                else
                {
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        resolved = JsonDocument.Parse(stream.ToArray());
        error = null;
        return true;
    }
}
