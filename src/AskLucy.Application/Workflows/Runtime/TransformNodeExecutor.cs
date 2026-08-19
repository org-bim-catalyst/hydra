using System.Text.Json;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Pure, <see cref="IWorkflowExpressionEvaluator"/>-backed field mapping (FR-018). Configuration
/// shape: <c>{"expression": "...", "outputField": "result"}</c>. <paramref name="context"/>.Node
/// carries the node's own configuration; the <c>input</c> parameter is the orchestrator's current
/// resolved-values snapshot (<see cref="WorkflowResolvedValues"/>) to evaluate the expression
/// against — never a second copy of the configuration.
/// </summary>
public sealed class TransformNodeExecutor(IWorkflowExpressionEvaluator expressionEvaluator) : IWorkflowNodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.Transform;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        if (!configuration.RootElement.TryGetProperty("expression", out var expressionElement) || expressionElement.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(WorkflowNodeExecutionResult.Failure("Transform node configuration is missing a required 'expression' string."));
        }

        var outputField = configuration.RootElement.TryGetProperty("outputField", out var outputFieldElement) && outputFieldElement.ValueKind == JsonValueKind.String
            ? outputFieldElement.GetString()!
            : "result";

        WorkflowExpressionNode ast;
        try
        {
            ast = expressionEvaluator.Parse(expressionElement.GetString()!);
        }
        catch (WorkflowExpressionParseException ex)
        {
            return Task.FromResult(WorkflowNodeExecutionResult.Failure($"Invalid transform expression: {ex.Message}"));
        }

        var resolvedValues = WorkflowResolvedValues.ParseInputDocument(input);

        WorkflowExpressionValue result;
        try
        {
            result = expressionEvaluator.Evaluate(ast, resolvedValues);
        }
        catch (WorkflowExpressionEvaluationException ex)
        {
            return Task.FromResult(WorkflowNodeExecutionResult.Failure($"Transform evaluation failed: {ex.Message}"));
        }

        var output = WorkflowResolvedValues.ToInputDocument(new Dictionary<string, WorkflowExpressionValue> { [outputField] = result });
        return Task.FromResult(WorkflowNodeExecutionResult.Success(output));
    }
}
