using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Workflows.Expressions;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Runtime;

/// <summary>
/// Validates data against either a declared JSON Schema (via <see cref="IJsonSchemaValidator"/>)
/// or a boolean workflow expression (via <see cref="IWorkflowExpressionEvaluator"/>), per
/// contracts/workflow-node-contract.md. Configuration is exactly one of:
/// <c>{"expression": "{{workflow.score}} &gt;= 0.8"}</c> — the expression must evaluate to a
/// boolean; or <c>{"schemaJson": {...}}</c> — validated against the orchestrator's current
/// flattened resolved-values snapshot (the same dotted-path object every executor receives as
/// <c>input</c>), not arbitrary nested JSON the expression engine cannot represent.
/// </summary>
public sealed class ValidationNodeExecutor(IWorkflowExpressionEvaluator expressionEvaluator, IJsonSchemaValidator schemaValidator) : IWorkflowNodeExecutor
{
    private const long MaxSchemaValidationSizeBytes = 1_000_000;

    public WorkflowNodeType NodeType => WorkflowNodeType.Validation;

    public Task<WorkflowNodeExecutionResult> ExecuteAsync(WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken = default)
    {
        using var configuration = JsonDocument.Parse(context.Node.ConfigurationJson);
        var root = configuration.RootElement;

        if (root.TryGetProperty("expression", out var expressionElement) && expressionElement.ValueKind == JsonValueKind.String)
        {
            return Task.FromResult(ValidateExpression(expressionElement.GetString()!, input));
        }

        if (root.TryGetProperty("schemaJson", out var schemaElement) && schemaElement.ValueKind == JsonValueKind.Object)
        {
            return Task.FromResult(ValidateSchema(schemaElement, input));
        }

        return Task.FromResult(WorkflowNodeExecutionResult.Failure("Validation node configuration requires either an 'expression' string or a 'schemaJson' object."));
    }

    private WorkflowNodeExecutionResult ValidateExpression(string expression, JsonDocument input)
    {
        var resolvedValues = WorkflowResolvedValues.ParseInputDocument(input);

        WorkflowExpressionNode ast;
        try
        {
            ast = expressionEvaluator.Parse(expression);
        }
        catch (WorkflowExpressionParseException ex)
        {
            return WorkflowNodeExecutionResult.Failure($"Invalid validation expression: {ex.Message}");
        }

        WorkflowExpressionValue result;
        try
        {
            result = expressionEvaluator.Evaluate(ast, resolvedValues);
        }
        catch (WorkflowExpressionEvaluationException ex)
        {
            return WorkflowNodeExecutionResult.Failure($"Validation evaluation failed: {ex.Message}");
        }

        if (result.Kind != WorkflowExpressionValueKind.Boolean)
        {
            return WorkflowNodeExecutionResult.Failure("Validation expression must evaluate to a boolean.");
        }

        return result.BooleanValue == true
            ? Valid()
            : WorkflowNodeExecutionResult.Failure($"Validation failed: '{expression}' evaluated to false.");
    }

    private WorkflowNodeExecutionResult ValidateSchema(JsonElement schema, JsonDocument input)
    {
        var violations = schemaValidator.Validate(schema, input.RootElement, MaxSchemaValidationSizeBytes);
        return violations.Count == 0 ? Valid() : WorkflowNodeExecutionResult.Failure("Validation failed: " + string.Join("; ", violations));
    }

    private static WorkflowNodeExecutionResult Valid() =>
        WorkflowNodeExecutionResult.Success(WorkflowResolvedValues.ToInputDocument(new Dictionary<string, WorkflowExpressionValue> { ["valid"] = WorkflowExpressionValue.OfBoolean(true) }));
}
