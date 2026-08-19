using System.Text.Json;
using AskLucy.Application.Agents.Tools;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Thin adapter over <c>FileReadTool</c>/<c>FileMetadataTool</c> — read/metadata only, never write
/// (research.md Decision 4). Configuration shape: <c>{"operation": "Read"|"Metadata",
/// "documentId": "..."}</c> — <c>documentId</c> may be a literal string or a <c>"{{...}}"</c>
/// workflow expression.
/// </summary>
public sealed class FileOperationNodeExecutor(AgentToolCatalog toolCatalog, IWorkflowExpressionEvaluator expressionEvaluator) : IWorkflowNodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.FileOperation;

    public async Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        var root = configuration.RootElement;

        if (!root.TryGetProperty("documentId", out var documentIdElement) || documentIdElement.ValueKind != JsonValueKind.String)
        {
            return WorkflowNodeExecutionResult.Failure("File Operation node configuration is missing a required 'documentId' string.");
        }

        var operation = root.TryGetProperty("operation", out var operationElement) && operationElement.ValueKind == JsonValueKind.String
            ? operationElement.GetString()
            : "Read";

        var toolName = operation?.Equals("Metadata", StringComparison.OrdinalIgnoreCase) == true ? "FileMetadataTool" : "FileReadTool";

        var resolvedValues = WorkflowResolvedValues.ParseInputDocument(input);
        if (!WorkflowCapabilityToolInvoker.TryResolveConfigString(documentIdElement.GetString(), expressionEvaluator, resolvedValues, out var documentId, out var error))
        {
            return WorkflowNodeExecutionResult.Failure(error!);
        }

        var tool = toolCatalog.Find(toolName);
        if (tool is null)
        {
            return WorkflowNodeExecutionResult.Failure($"{toolName} is not registered.");
        }

        using var toolInput = JsonSerializer.SerializeToDocument(new { documentId });
        return await WorkflowCapabilityToolInvoker.InvokeAsync(tool, context, toolInput, cancellationToken);
    }
}
