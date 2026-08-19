using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Thin adapter over the <c>McpToolAdapter</c>-produced <see cref="IAgentTool"/> for a configured
/// MCP server/tool (research.md Decision 1) — never talks to <c>McpToolRegistry</c>/an MCP server
/// directly, exactly as <c>AgentToolCatalog.Find</c> already merges native and MCP tools
/// transparently for callers. Configuration shape: <c>{"toolName": "mcp:{serverId}:{toolName}",
/// "input": {...}}</c> — every string property of <c>input</c> may be a literal or a
/// <c>"{{...}}"</c> workflow expression.
/// </summary>
public sealed class McpToolNodeExecutor(AgentToolCatalog toolCatalog, IWorkflowExpressionEvaluator expressionEvaluator) : IWorkflowNodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.McpTool;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        var root = configuration.RootElement;

        if (!root.TryGetProperty("toolName", out var toolNameElement) || toolNameElement.ValueKind != JsonValueKind.String)
        {
            return WorkflowNodeExecutionResult.Failure("MCP Tool node configuration is missing a required 'toolName' string.");
        }

        var toolName = toolNameElement.GetString()!;
        if (!toolName.StartsWith("mcp:", StringComparison.Ordinal))
        {
            return WorkflowNodeExecutionResult.Failure($"'{toolName}' is not an MCP tool name (expected the 'mcp:{{serverId}}:{{toolName}}' form) — use a Native Tool node for non-MCP tools.");
        }

        var tool = toolCatalog.Find(toolName);
        if (tool is null)
        {
            return WorkflowNodeExecutionResult.Failure($"No active MCP tool named '{toolName}' was found.");
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
