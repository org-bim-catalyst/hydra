# Contract: Sandboxed Expression Engine

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decision 6)

FR-027/FR-062: this grammar is the **entire** surface a workflow author can use for conditions, transformations, and mappings. It is closed by design — nothing outside this document's grammar can ever be expressed, parsed, or evaluated. No `eval`, no reflection, no dynamic type loading, no arbitrary function registration at runtime.

## Grammar (EBNF, informal)

```
expression   := logicalOr
logicalOr    := logicalAnd ( "OR" logicalAnd )*
logicalAnd   := logicalNot ( "AND" logicalNot )*
logicalNot   := "NOT" logicalNot | comparison
comparison   := term ( ( "==" | "!=" | "<" | "<=" | ">" | ">=" ) term )?
term         := literal | reference | functionCall | "(" expression ")"
literal      := string | number | boolean | "null"
reference    := "{{" path "}}"                 // e.g. {{steps.classify.category}}, {{workflow.threshold}}
functionCall := functionName "(" ( expression ( "," expression )* )? ")"
functionName := "concat" | "length" | "contains" | "isEmpty"
```

- `path` resolves against `WorkflowExecution.VariablesJson` (`workflow.*`), a prior node's `WorkflowExecutionNode.OutputJson` (`steps.{nodeKey}.*`), or declared `WorkflowVariable`s of kind `UserInput`/`EnvironmentConfiguration`/`SystemContext` — never an arbitrary CLR member, reflection path, or file/network resource.
- The four whitelisted functions are pure, side-effect-free, and fixed at compile time in `WorkflowExpressionEvaluator` — the grammar has no syntax for declaring or invoking any function outside this list. Adding a fifth function is a code change to the evaluator (reviewed like any other code change), never a workflow-author-supplied capability.

## Implementation shape

```csharp
public interface IWorkflowExpressionEvaluator
{
    // Throws WorkflowExpressionParseException for a syntactically invalid expression.
    WorkflowExpressionAst Parse(string expression);

    // Static type check against declared WorkflowVariable/node-output types (FR-028) — run at
    // publish/validate time (ValidateWorkflowCommand, workflows-api.md), never first-discovered at runtime.
    IReadOnlyList<string> ValidateTypes(WorkflowExpressionAst ast, IReadOnlyDictionary<string, WorkflowVariableType> knownTypes);

    // Pure evaluation against the live execution's resolved variable/output values.
    WorkflowExpressionValue Evaluate(WorkflowExpressionAst ast, IReadOnlyDictionary<string, WorkflowExpressionValue> resolvedValues);
}
```

`Parse` and `Evaluate` are pure, allocation-light tree-walking operations over a hand-written recursive-descent parser's AST — no `System.Reflection`, no `System.CodeDom`, no `Microsoft.CodeAnalysis.CSharp.Scripting`, no third-party expression-evaluation package. This is a genuinely new ~200-line component (research.md Decision 6), not a wrapper around an existing capability, because no safe existing option covers "evaluate a user-authored boolean/comparison expression without ever risking arbitrary code execution."

## Where it's used

| Consumer | Grammar subset actually needed |
|---|---|
| `ConditionNodeExecutor` | Full grammar — must resolve to a `boolean` |
| `TransformNodeExecutor` | Full grammar — resolves to any `WorkflowVariableType`, mapped into the node's declared output field(s) |
| `ValidationNodeExecutor` | Full grammar, typically comparison/function calls against required-field presence |
| `WorkflowNode.IdempotencyKeyExpression` (FR-043) | Typically `concat(...)` over a small set of resolved input fields — must resolve to a `string` |
| Loop bound expressions (bounded iteration, FR-032) | Comparison against a `Collection`-typed variable's resolved length, or a fixed numeric literal — never an open-ended predicate |

## Security properties (verified by the Security Tests category in spec.md's Testing section)

1. **No code execution**: the parser's grammar has no production that can express a statement, a loop, an assignment, a method call outside the four whitelisted pure functions, or a type/namespace reference. This is verified by a security test suite (`WorkflowExpressionEngineSecurityTests`) that asserts a representative set of known injection-style payloads (e.g., attempts to reference `System.Diagnostics.Process`, attempts to use string concatenation to smuggle a second expression, attempts to reference `{{steps.x.__proto__}}`-style prototype-pollution-flavored paths) all fail to parse or fail type validation, never silently no-op or partially execute.
2. **Bounded evaluation**: every `reference` path resolves against a fixed, pre-supplied dictionary (`resolvedValues`) — there is no mechanism for an expression to trigger a new I/O call, database query, or network request during `Evaluate`. Evaluation is synchronous, side-effect-free, and cannot itself time out in a way that matters (a `WorkflowNode.TimeoutSeconds` still bounds the *node*, not the expression specifically, since the expression alone cannot hang).
3. **Untrusted content stays data** (FR-060): a `reference` can point at a prior node's output — including RAG/MCP/document content — but the expression engine only ever reads that value as a typed literal for comparison/transformation; there is no path by which resolving a reference could cause the *expression itself* to change shape (the AST is parsed once, from the workflow author's own configuration, before any execution-time value exists — external content can influence what a condition *evaluates to*, never what the condition *is*).
