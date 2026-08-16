using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Thin adapter over <c>DocumentSearchTool</c> (research.md Decision 1). Configuration shape:
/// <c>{"query": "..."}</c> — <c>query</c> may be a literal string or a <c>"{{...}}"</c> workflow
/// expression.
/// </summary>
public sealed class DocumentProcessingNodeExecutor(AgentToolCatalog toolCatalog, IWorkflowExpressionEvaluator expressionEvaluator) : IWorkflowNodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.DocumentProcessing;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        var root = configuration.RootElement;

        if (!root.TryGetProperty("query", out var queryElement) || queryElement.ValueKind != JsonValueKind.String)
        {
            return WorkflowNodeExecutionResult.Failure("Document Processing node configuration is missing a required 'query' string.");
        }

        var resolvedValues = WorkflowResolvedValues.ParseInputDocument(input);
        if (!WorkflowCapabilityToolInvoker.TryResolveConfigString(queryElement.GetString(), expressionEvaluator, resolvedValues, out var query, out var error))
        {
            return WorkflowNodeExecutionResult.Failure(error!);
        }

        var tool = toolCatalog.Find("DocumentSearchTool");
        if (tool is null)
        {
            return WorkflowNodeExecutionResult.Failure("DocumentSearchTool is not registered.");
        }

        using var toolInput = JsonSerializer.SerializeToDocument(new { query });
        return await WorkflowCapabilityToolInvoker.InvokeAsync(tool, context, toolInput, cancellationToken);
    }
}
