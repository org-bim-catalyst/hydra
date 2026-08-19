using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Thin adapter over any other DI-registered <see cref="IAgentTool"/> (e.g. <c>ConversationTool</c>,
/// research.md Decision 1). Configuration shape: <c>{"toolName": "...", "input": {...}}</c> —
/// every string property of <c>input</c> may be a literal or a <c>"{{...}}"</c> workflow
/// expression. <c>toolName</c> must not be MCP-namespaced (<c>"mcp:..."</c>) — use an MCP Tool
/// node for those instead, so the workflow's own node-type choice always matches which underlying
/// system a call actually reaches.
/// </summary>
public sealed class NativeToolNodeExecutor(AgentToolCatalog toolCatalog, IWorkflowExpressionEvaluator expressionEvaluator) : IWorkflowNodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.NativeTool;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        var root = configuration.RootElement;

        if (!root.TryGetProperty("toolName", out var toolNameElement) || toolNameElement.ValueKind != JsonValueKind.String)
        {
            return WorkflowNodeExecutionResult.Failure("Native Tool node configuration is missing a required 'toolName' string.");
        }

        var toolName = toolNameElement.GetString()!;
        if (toolName.StartsWith("mcp:", StringComparison.Ordinal))
        {
            return WorkflowNodeExecutionResult.Failure($"'{toolName}' is an MCP tool — use an MCP Tool node for it instead.");
        }

        var tool = toolCatalog.Find(toolName);
        if (tool is null)
        {
            return WorkflowNodeExecutionResult.Failure($"No native tool named '{toolName}' is registered.");
        }

        var resolvedValues = WorkflowResolvedValues.ParseInputDocument(input);
        using var emptyInputFallback = JsonDocument.Parse("{}");
        var toolInputElement = root.TryGetProperty("input", out var inputElement) && inputElement.ValueKind == JsonValueKind.Object
            ? inputElement
            : emptyInputFallback.RootElement;

        if (!WorkflowCapabilityToolInvoker.TryResolveConfigObject(toolInputElement, expressionEvaluator, resolvedValues, out var resolvedInput, out var error))
        {
            return WorkflowNodeExecutionResult.Failure(error!);
        }

        using var toolInput = resolvedInput!;
        return await WorkflowCapabilityToolInvoker.InvokeAsync(tool, context, toolInput, cancellationToken);
    }
}
