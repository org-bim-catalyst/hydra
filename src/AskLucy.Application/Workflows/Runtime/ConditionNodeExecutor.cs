using System.Text.Json;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Evaluates a boolean workflow expression (FR-029, contracts/workflow-node-contract.md).
/// Configuration shape: <c>{"expression": "{{steps.classify.category}} == \"urgent\""}</c>.
/// Routing to exactly one branch and marking the unchosen one <c>Skipped</c> is
/// <see cref="WorkflowExecutionOrchestrator"/>'s job (it needs the branch <see cref="WorkflowConnection.BranchLabel"/>s
/// this executor has no access to) — this class only produces the <c>{"result": bool}</c> the
/// orchestrator routes on.
/// </summary>
public sealed class ConditionNodeExecutor(IWorkflowExpressionEvaluator expressionEvaluator) : IWorkflowNodeExecutor
{
    public WorkflowNodeType NodeType => WorkflowNodeType.Condition;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        if (!configuration.RootElement.TryGetProperty("expression", out var expressionElement) || expressionElement.ValueKind != JsonValueKind.String)
        {
            return Task.FromResult(WorkflowNodeExecutionResult.Failure("Condition node configuration is missing a required 'expression' string."));
        }

        var resolvedValues = WorkflowResolvedValues.ParseInputDocument(input);

        WorkflowExpressionNode ast;
        try
        {
            ast = expressionEvaluator.Parse(expressionElement.GetString()!);
        }
        catch (WorkflowExpressionParseException ex)
        {
            return Task.FromResult(WorkflowNodeExecutionResult.Failure($"Invalid condition expression: {ex.Message}"));
        }

        WorkflowExpressionValue result;
        try
        {
            result = expressionEvaluator.Evaluate(ast, resolvedValues);
        }
        catch (WorkflowExpressionEvaluationException ex)
        {
            return Task.FromResult(WorkflowNodeExecutionResult.Failure($"Condition evaluation failed: {ex.Message}"));
        }

        if (result.Kind != WorkflowExpressionValueKind.Boolean)
        {
            return Task.FromResult(WorkflowNodeExecutionResult.Failure("Condition expression must evaluate to a boolean."));
        }

        var output = WorkflowResolvedValues.ToInputDocument(new Dictionary<string, WorkflowExpressionValue> { ["result"] = result });
        return Task.FromResult(WorkflowNodeExecutionResult.Success(output));
    }
}
