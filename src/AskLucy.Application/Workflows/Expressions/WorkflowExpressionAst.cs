using System.Globalization;
using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Expressions;

/// <summary>
/// The closed grammar's AST node types (contracts/workflow-expression-engine.md,
/// research.md Decision 6) — literal, reference, comparison, logical (AND/OR/NOT), and function
/// call are the *only* expressible constructs. There is deliberately no statement, assignment,
/// loop, or arbitrary-method-call node — the grammar cannot express one, by construction.
/// </summary>
public abstract record WorkflowExpressionNode;

public sealed record LiteralExpressionNode(WorkflowExpressionValue Value) : WorkflowExpressionNode;

/// <summary>A <c>{{path}}</c> reference, e.g. <c>steps.classify.category</c> or <c>workflow.threshold</c>.</summary>
public sealed record ReferenceExpressionNode(string Path) : WorkflowExpressionNode;

/// <summary><see cref="Operator"/> is one of <c>==</c>, <c>!=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>.</summary>
public sealed record ComparisonExpressionNode(WorkflowExpressionNode Left, string Operator, WorkflowExpressionNode Right) : WorkflowExpressionNode;

/// <summary><see cref="Operator"/> is <c>AND</c>, <c>OR</c>, or <c>NOT</c>; <see cref="Right"/> is null only for <c>NOT</c>.</summary>
public sealed record LogicalExpressionNode(string Operator, WorkflowExpressionNode Left, WorkflowExpressionNode? Right) : WorkflowExpressionNode;

/// <summary><see cref="FunctionName"/> is always one of the four whitelisted pure functions: <c>concat</c>, <c>length</c>, <c>contains</c>, <c>isEmpty</c> — the parser never produces any other value here.</summary>
public sealed record FunctionCallExpressionNode(string FunctionName, IReadOnlyList<WorkflowExpressionNode> Arguments) : WorkflowExpressionNode;

// CA1720: not renamed — this enum represents runtime value-type kinds (the expression engine's
// own closed type system), and "String" is the correct, idiomatic name for that kind; renaming it
// away from the actual data type it represents would hurt readability for no benefit.
#pragma warning disable CA1720
public enum WorkflowExpressionValueKind
{
    String,
    Number,
    Boolean,
    Null,
    Collection,
}
#pragma warning restore CA1720

/// <summary>A runtime value produced by parsing a literal or resolving a reference/evaluating an expression — a small, closed tagged union, never an arbitrary CLR object.</summary>
public sealed record WorkflowExpressionValue(
    WorkflowExpressionValueKind Kind,
    string? StringValue = null,
    double? NumberValue = null,
    bool? BooleanValue = null,
    IReadOnlyList<WorkflowExpressionValue>? CollectionValue = null)
{
    public static readonly WorkflowExpressionValue Null = new(WorkflowExpressionValueKind.Null);

    public static WorkflowExpressionValue OfString(string value) => new(WorkflowExpressionValueKind.String, StringValue: value);

    public static WorkflowExpressionValue OfNumber(double value) => new(WorkflowExpressionValueKind.Number, NumberValue: value);

    public static WorkflowExpressionValue OfBoolean(bool value) => new(WorkflowExpressionValueKind.Boolean, BooleanValue: value);

    public static WorkflowExpressionValue OfCollection(IReadOnlyList<WorkflowExpressionValue> value) => new(WorkflowExpressionValueKind.Collection, CollectionValue: value);

    /// <summary>A safe, deterministic textual rendering — used by <c>concat</c> and for equality/error messages; never invokes arbitrary <c>ToString()</c> overrides on untrusted CLR types, since every case is closed.</summary>
    public string ToDisplayString() => Kind switch
    {
        WorkflowExpressionValueKind.String => StringValue ?? string.Empty,
        WorkflowExpressionValueKind.Number => NumberValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        WorkflowExpressionValueKind.Boolean => (BooleanValue ?? false) ? "true" : "false",
        WorkflowExpressionValueKind.Null => string.Empty,
        WorkflowExpressionValueKind.Collection => string.Join(",", (CollectionValue ?? []).Select(v => v.ToDisplayString())),
        _ => string.Empty,
    };

    /// <summary>Maps to the closest <see cref="WorkflowVariableType"/> for static type-checking (FR-028) — <see cref="WorkflowExpressionValueKind.Null"/> has no fixed type and matches any.</summary>
    public WorkflowVariableType? InferVariableType() => Kind switch
    {
        WorkflowExpressionValueKind.String => WorkflowVariableType.String,
        WorkflowExpressionValueKind.Number => WorkflowVariableType.Number,
        WorkflowExpressionValueKind.Boolean => WorkflowVariableType.Boolean,
        WorkflowExpressionValueKind.Collection => WorkflowVariableType.Collection,
        _ => null,
    };
}

/// <summary>Thrown by <see cref="IWorkflowExpressionEvaluator.Parse"/> for a syntactically invalid expression (FR-027/FR-028).</summary>
public sealed class WorkflowExpressionParseException(string message) : Exception(message);

/// <summary>Thrown by <see cref="IWorkflowExpressionEvaluator.Evaluate"/> when a reference cannot be resolved or an operator is applied to an incompatible runtime value — never silently coerced (spec.md Edge Cases).</summary>
public sealed class WorkflowExpressionEvaluationException(string message) : Exception(message);
