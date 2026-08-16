using AskLucy.Domain.Workflows;

namespace AskLucy.Application.Workflows.Expressions;

/// <summary>
/// The sandboxed expression engine (FR-027/FR-028, contracts/workflow-expression-engine.md,
/// research.md Decision 6). Backs Condition/Transform/Validation node configuration and
/// idempotency-key resolution (FR-043). The grammar this evaluates is closed by design — see
/// contracts/workflow-expression-engine.md's EBNF — so this interface can never execute arbitrary
/// user-supplied C# or JavaScript (FR-062).
/// </summary>
public interface IWorkflowExpressionEvaluator
{
    /// <summary>Throws <see cref="WorkflowExpressionParseException"/> for a syntactically invalid expression.</summary>
    WorkflowExpressionNode Parse(string expression);

    /// <summary>Static type check against declared <see cref="WorkflowVariable"/>/node-output types (FR-028) — run at publish/validate time (<c>WorkflowGraphValidator</c>), never first-discovered at runtime. Returns one message per violation; empty when valid.</summary>
    IReadOnlyList<string> ValidateTypes(WorkflowExpressionNode ast, IReadOnlyDictionary<string, WorkflowVariableType> knownTypes);

    /// <summary>Pure evaluation against the live execution's resolved variable/output values. Throws <see cref="WorkflowExpressionEvaluationException"/> for an unresolved reference or an operator applied to an incompatible value — never silently coerces.</summary>
    WorkflowExpressionValue Evaluate(WorkflowExpressionNode ast, IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues);
}
