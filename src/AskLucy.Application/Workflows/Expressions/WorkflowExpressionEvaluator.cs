using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Expressions;

/// <summary>Implementation of <see cref="IWorkflowExpressionEvaluator"/> — see that interface and contracts/workflow-expression-engine.md for the contract this fulfils.</summary>
public sealed class WorkflowExpressionEvaluator : IWorkflowExpressionEvaluator
{
    public WorkflowExpressionNode Parse(string expression) => WorkflowExpressionParser.Parse(expression);

    public IReadOnlyList<string> ValidateTypes(WorkflowExpressionNode ast, IReadOnlyDictionary<string, WorkflowVariableType> knownTypes)
    {
        var errors = new List<string>();
        InferType(ast, knownTypes, errors);
        return errors;
    }

    private static WorkflowVariableType? InferType(WorkflowExpressionNode node, IReadOnlyDictionary<string, WorkflowVariableType> knownTypes, List<string> errors)
    {
        switch (node)
        {
            case LiteralExpressionNode literal:
                return literal.Value.InferVariableType();

            case ReferenceExpressionNode reference:
                if (knownTypes.TryGetValue(reference.Path, out var referenceType))
                {
                    return referenceType;
                }

                errors.Add($"Unknown reference '{{{{{reference.Path}}}}}'.");
                return null;

            case ComparisonExpressionNode comparison:
                var leftType = InferType(comparison.Left, knownTypes, errors);
                var rightType = InferType(comparison.Right, knownTypes, errors);
                if (comparison.Operator is "<" or "<=" or ">" or ">=")
                {
                    if (leftType is not (null or WorkflowVariableType.Number) || rightType is not (null or WorkflowVariableType.Number))
                    {
                        errors.Add($"Operator '{comparison.Operator}' requires both operands to be Number.");
                    }
                }
                else if (leftType is not null && rightType is not null && leftType != rightType)
                {
                    errors.Add($"Cannot compare {leftType} with {rightType}.");
                }

                return WorkflowVariableType.Boolean;

            case LogicalExpressionNode logical:
                var leftLogicalType = InferType(logical.Left, knownTypes, errors);
                if (leftLogicalType is not (null or WorkflowVariableType.Boolean))
                {
                    errors.Add($"Operator '{logical.Operator}' requires a Boolean operand.");
                }

                if (logical.Right is not null)
                {
                    var rightLogicalType = InferType(logical.Right, knownTypes, errors);
                    if (rightLogicalType is not (null or WorkflowVariableType.Boolean))
                    {
                        errors.Add($"Operator '{logical.Operator}' requires a Boolean operand.");
                    }
                }

                return WorkflowVariableType.Boolean;

            case FunctionCallExpressionNode functionCall:
                foreach (var argument in functionCall.Arguments)
                {
                    InferType(argument, knownTypes, errors);
                }

                return functionCall.FunctionName switch
                {
                    "concat" => WorkflowVariableType.String,
                    "length" => WorkflowVariableType.Number,
                    "contains" => WorkflowVariableType.Boolean,
                    "isEmpty" => WorkflowVariableType.Boolean,
                    _ => null,
                };

            default:
                return null;
        }
    }

    public WorkflowExpressionValue Evaluate(WorkflowExpressionNode ast, IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues) => ast switch
    {
        LiteralExpressionNode literal => literal.Value,
        ReferenceExpressionNode reference => resolvedValues.TryGetValue(reference.Path, out var value)
            ? value
            : throw new WorkflowExpressionEvaluationException($"Reference '{{{{{reference.Path}}}}}' could not be resolved."),
        ComparisonExpressionNode comparison => EvaluateComparison(comparison, resolvedValues),
        LogicalExpressionNode logical => EvaluateLogical(logical, resolvedValues),
        FunctionCallExpressionNode functionCall => EvaluateFunctionCall(functionCall, resolvedValues),
        _ => throw new WorkflowExpressionEvaluationException("Unrecognized expression node."),
    };

    private WorkflowExpressionValue EvaluateComparison(ComparisonExpressionNode comparison, IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues)
    {
        var left = Evaluate(comparison.Left, resolvedValues);
        var right = Evaluate(comparison.Right, resolvedValues);

        if (comparison.Operator is "<" or "<=" or ">" or ">=")
        {
            if (left.Kind != WorkflowExpressionValueKind.Number || right.Kind != WorkflowExpressionValueKind.Number)
            {
                throw new WorkflowExpressionEvaluationException($"Operator '{comparison.Operator}' requires both operands to be Number.");
            }

            var l = left.NumberValue!.Value;
            var r = right.NumberValue!.Value;
            var result = comparison.Operator switch
            {
                "<" => l < r,
                "<=" => l <= r,
                ">" => l > r,
                ">=" => l >= r,
                _ => throw new WorkflowExpressionEvaluationException($"Unknown operator '{comparison.Operator}'."),
            };

            return WorkflowExpressionValue.OfBoolean(result);
        }

        var equal = ValuesEqual(left, right);
        return WorkflowExpressionValue.OfBoolean(comparison.Operator == "==" ? equal : !equal);
    }

    private static bool ValuesEqual(WorkflowExpressionValue left, WorkflowExpressionValue right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            WorkflowExpressionValueKind.String => string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal),
            WorkflowExpressionValueKind.Number => left.NumberValue == right.NumberValue,
            WorkflowExpressionValueKind.Boolean => left.BooleanValue == right.BooleanValue,
            WorkflowExpressionValueKind.Null => true,
            _ => false,
        };
    }

    private WorkflowExpressionValue EvaluateLogical(LogicalExpressionNode logical, IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues)
    {
        var left = RequireBoolean(Evaluate(logical.Left, resolvedValues), logical.Operator);

        return logical.Operator switch
        {
            "NOT" => WorkflowExpressionValue.OfBoolean(!left),
            "AND" => WorkflowExpressionValue.OfBoolean(left && RequireBoolean(Evaluate(logical.Right!, resolvedValues), logical.Operator)),
            "OR" => WorkflowExpressionValue.OfBoolean(left || RequireBoolean(Evaluate(logical.Right!, resolvedValues), logical.Operator)),
            _ => throw new WorkflowExpressionEvaluationException($"Unknown logical operator '{logical.Operator}'."),
        };
    }

    private static bool RequireBoolean(WorkflowExpressionValue value, string operatorName)
    {
        if (value.Kind != WorkflowExpressionValueKind.Boolean)
        {
            throw new WorkflowExpressionEvaluationException($"Operator '{operatorName}' requires a Boolean operand.");
        }

        return value.BooleanValue!.Value;
    }

    private WorkflowExpressionValue EvaluateFunctionCall(FunctionCallExpressionNode functionCall, IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues)
    {
        var arguments = functionCall.Arguments.Select(a => Evaluate(a, resolvedValues)).ToList();

        return functionCall.FunctionName switch
        {
            "concat" => WorkflowExpressionValue.OfString(string.Concat(arguments.Select(a => a.ToDisplayString()))),
            "length" => WorkflowExpressionValue.OfNumber(EvaluateLength(RequireSingleArgument(arguments, "length"))),
            "contains" => WorkflowExpressionValue.OfBoolean(EvaluateContains(RequireArguments(arguments, "contains", 2))),
            "isEmpty" => WorkflowExpressionValue.OfBoolean(EvaluateIsEmpty(RequireSingleArgument(arguments, "isEmpty"))),
            _ => throw new WorkflowExpressionEvaluationException($"'{functionCall.FunctionName}' is not a recognized function."),
        };
    }

    private static WorkflowExpressionValue RequireSingleArgument(List<WorkflowExpressionValue> arguments, string functionName) =>
        arguments.Count == 1 ? arguments[0] : throw new WorkflowExpressionEvaluationException($"'{functionName}' requires exactly one argument.");

    private static List<WorkflowExpressionValue> RequireArguments(List<WorkflowExpressionValue> arguments, string functionName, int count) =>
        arguments.Count == count ? arguments : throw new WorkflowExpressionEvaluationException($"'{functionName}' requires exactly {count} arguments.");

    private static double EvaluateLength(WorkflowExpressionValue value) => value.Kind switch
    {
        WorkflowExpressionValueKind.String => value.StringValue?.Length ?? 0,
        WorkflowExpressionValueKind.Collection => value.CollectionValue?.Count ?? 0,
        _ => throw new WorkflowExpressionEvaluationException("'length' requires a String or Collection argument."),
    };

    private static bool EvaluateContains(List<WorkflowExpressionValue> arguments)
    {
        var (haystack, needle) = (arguments[0], arguments[1]);

        return haystack.Kind switch
        {
            WorkflowExpressionValueKind.String when needle.Kind == WorkflowExpressionValueKind.String =>
                (haystack.StringValue ?? string.Empty).Contains(needle.StringValue ?? string.Empty, StringComparison.Ordinal),
            WorkflowExpressionValueKind.Collection => (haystack.CollectionValue ?? []).Any(item => ValuesEqual(item, needle)),
            _ => throw new WorkflowExpressionEvaluationException("'contains' requires a String haystack with a String needle, or a Collection haystack."),
        };
    }

    private static bool EvaluateIsEmpty(WorkflowExpressionValue value) => value.Kind switch
    {
        WorkflowExpressionValueKind.String => string.IsNullOrEmpty(value.StringValue),
        WorkflowExpressionValueKind.Collection => (value.CollectionValue?.Count ?? 0) == 0,
        WorkflowExpressionValueKind.Null => true,
        _ => throw new WorkflowExpressionEvaluationException("'isEmpty' requires a String, Collection, or null argument."),
    };
}
