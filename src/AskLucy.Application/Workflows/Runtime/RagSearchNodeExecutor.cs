using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Thin adapter over <c>KnowledgeSearchTool</c> (research.md Decision 1, contracts/workflow-node-contract.md).
/// Configuration shape: <c>{"query": "...", "knowledgeBaseIds": ["..."]}</c> — <c>query</c> may be
/// a literal string or a <c>"{{...}}"</c> workflow expression. Every guarantee
/// <c>agent-tool-contract.md</c> documents (input validation, permission check, approval gate,
/// output validation) applies unchanged, because this is the same <see cref="IAgentTool"/> call an
/// Agent would make, not a re-implementation.
/// </summary>
public sealed class RagSearchNodeExecutor(AgentToolCatalog toolCatalog, IWorkflowExpressionEvaluator expressionEvaluator) : IWorkflowNodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.RagSearch;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        var root = configuration.RootElement;

        if (!root.TryGetProperty("query", out var queryElement) || queryElement.ValueKind != JsonValueKind.String)
        {
            return WorkflowNodeExecutionResult.Failure("RAG Search node configuration is missing a required 'query' string.");
        }

        var knowledgeBaseIds = root.TryGetProperty("knowledgeBaseIds", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array
            ? idsElement.EnumerateArray().Select(e => e.GetString()).Where(id => id is not null).ToArray()
            : [];

        var resolvedValues = WorkflowResolvedValues.ParseInputDocument(input);
        if (!WorkflowCapabilityToolInvoker.TryResolveConfigString(queryElement.GetString(), expressionEvaluator, resolvedValues, out var query, out var error))
        {
            return WorkflowNodeExecutionResult.Failure(error!);
        }

        var tool = toolCatalog.Find("KnowledgeSearchTool");
        if (tool is null)
        {
            return WorkflowNodeExecutionResult.Failure("KnowledgeSearchTool is not registered.");
        }

        using var toolInput = JsonSerializer.SerializeToDocument(new { query, knowledgeBaseIds });
        return await WorkflowCapabilityToolInvoker.InvokeAsync(tool, context, toolInput, cancellationToken);
    }
}
