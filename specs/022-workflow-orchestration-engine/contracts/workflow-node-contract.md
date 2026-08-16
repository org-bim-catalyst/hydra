# Contract: `IWorkflowNodeExecutor` and Node Dispatch

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decisions 1–5, 9, 13, 14)

## Interface

`src/AskLucy.Application/Workflows/Runtime/IWorkflowNodeExecutor.cs`:

```csharp
public interface IWorkflowNodeExecutor
{
    WorkflowNodeType NodeType { get; }           // which WorkflowNode.NodeType this executor handles

    Task<WorkflowNodeExecutionResult> ExecuteAsync(
        WorkflowNodeExecutionContext context, JsonDocument input, CancellationToken cancellationToken);
}

public sealed record WorkflowNodeExecutionContext(
    Guid WorkflowExecutionId, Guid WorkflowExecutionNodeId, string UserId,
    Guid WorkflowId, Guid WorkflowVersionId, WorkflowNode Node);

public sealed record WorkflowNodeExecutionResult(bool Succeeded, JsonDocument? Output, string? FailureReason, bool RequiresApproval = false)
{
    public static WorkflowNodeExecutionResult Success(JsonDocument output) => new(true, output, null);
    public static WorkflowNodeExecutionResult Failure(string reason) => new(false, null, reason);
}
```

One `IWorkflowNodeExecutor` implementation per `WorkflowNodeType`, resolved by `WorkflowExecutionOrchestrator` via a small `WorkflowNodeExecutorRegistry` (DI-collection lookup by `NodeType`, same shape `AgentToolCatalog.Find(name)` already uses) — a new node type is a new registered class, never an edit to the orchestrator (constitution §2.II OCP), matching `agent-tool-contract.md`'s own framing exactly.

## Executor implementations and what they wrap (research.md Decision 1)

| `WorkflowNodeType` | Executor | Wraps | New code? |
|---|---|---|---|
| `RagSearch` | `RagSearchNodeExecutor` | `KnowledgeSearchTool` (`IAgentTool`, via `AgentToolCatalog`) | Thin adapter only |
| `MemorySearch` | `MemorySearchNodeExecutor` | `MemorySearchTool` | Thin adapter only |
| `DocumentProcessing` | `DocumentProcessingNodeExecutor` | `DocumentSearchTool` | Thin adapter only |
| `FileOperation` | `FileOperationNodeExecutor` | `FileReadTool` / `FileMetadataTool` (read/metadata only, research.md Decision 4) | Thin adapter only |
| `McpTool` | `McpToolNodeExecutor` | The `McpToolAdapter`-produced `IAgentTool` for the configured server/tool, via `McpToolRegistry` | Thin adapter only |
| `NativeTool` | `NativeToolNodeExecutor` | Any other `AgentToolCatalog` entry (e.g. `ConversationTool`) | Thin adapter only |
| `AiPrompt` | `PromptNodeExecutor` | `IPromptRepository` + `PromptVariableResolver` + `IAIProvider`/`IAIProviderResolver` | New (research.md Decision 2) |
| `AiAgent` | `AgentNodeExecutor` | `AgentExecutionOrchestrator.RunAsync`, invoked in-process | New, thin (research.md Decision 3) |
| `Condition` | `ConditionNodeExecutor` | `WorkflowExpressionEvaluator` (see workflow-expression-engine.md) | New, pure |
| `Transform` | `TransformNodeExecutor` | `WorkflowExpressionEvaluator` | New, pure |
| `Validation` | `ValidationNodeExecutor` | `IJsonSchemaValidator` (existing, spec 021) or `WorkflowExpressionEvaluator` | New, pure |
| `Merge` | `MergeNodeExecutor` | In-process `Task.WhenAll`/`Task.WhenAny` accumulation (research.md Decision 9) | New orchestration only |
| `HumanApproval` | Not a capability call — handled directly by `WorkflowExecutionOrchestrator`'s pause/resume path | `WorkflowApproval` (research.md Decision 5) | New |
| `Parallel` | Not a capability call — handled directly by `WorkflowExecutionOrchestrator`'s branch-fan-out logic | `SemaphoreSlim`-gated `Task.WhenAll` (research.md Decision 9) | New orchestration only |
| `Start` / `End` / `Delay` | Structural markers / architectural placeholder | N/A | New, trivial |

The seven "thin adapter" rows above validate their `WorkflowNode.ConfigurationJson`-selected target (Knowledge Base id / server+tool name / etc.), build the exact `AgentToolExecutionContext` the underlying `IAgentTool.ExecuteAsync` already expects, and delegate — **all five runtime guarantees `agent-tool-contract.md` documents (input validation, permission check, approval gate, output validation, duplicate-call detection) already apply**, because it is the same call, not a re-implementation.

## Runtime contract (enforced by `WorkflowExecutionOrchestrator`, not by each executor)

1. **Input resolution** (FR-025): every `{{steps.node_key.field}}`/`{{workflow.variable}}` reference in the node's configured input is resolved from `WorkflowExecution.VariablesJson` + prior `WorkflowExecutionNode.OutputJson` values before `ExecuteAsync` is called; an unresolved reference at this point is a runtime bug the FR-016 publish-time validator should already have caught, and fails the node with a standardized error rather than substituting a default (Edge Cases).
2. **Approval gate** (FR-033–FR-037, research.md Decision 5): for a capability-wrapping node, the *underlying* `IAgentTool.RiskLevel` — not the workflow author's own node-level approval setting — determines the platform-mandatory baseline; `High`/`Critical` always pauses unless a matching `WorkflowPolicy` (or the underlying tool's own `AgentPolicy`, since the call is the same one the Agent Runtime would make) applies. A `HumanApproval` node type always pauses regardless of any underlying risk level — that is its entire purpose.
3. **Idempotency check** (FR-043, research.md Decision 13): before *retrying* a node whose underlying permissions are mutating, the orchestrator resolves `WorkflowNode.IdempotencyKeyExpression` (if set) and checks for a prior successful `WorkflowExecutionNode` attempt with the same key; if found, `ExecuteAsync` is not called again — the prior `OutputJson` is reused.
4. **Retry** (FR-040): on failure, retried per `WorkflowNode.RetryPolicyJson` up to its configured maximum with exponential backoff, exactly mirroring `AgentExecutionOrchestrator.ExecuteToolWithRetryAsync`'s shape; a node whose `RequiredPermissionsJson` marks it non-idempotent and has no `IdempotencyKeyExpression` is retried at most once per FR-040's "do not blindly retry" rule.
5. **Timeout** (FR-041): a per-node `CancellationTokenSource` linked to `WorkflowNode.TimeoutSeconds` (or the `WorkflowRuntimeOptions` default) wraps the call; on expiry the node fails with `WorkflowErrorCategory.Timeout` and the configured failure policy applies.
6. **Compensation** (FR-042, research.md Decision 14): on a `Compensate` workflow-level failure strategy, already-`Completed` nodes with a `CompensatingNodeId` are re-dispatched through this exact same executor-registry path, in reverse completion order, before the execution is finalized `Failed`.

Because all six live in the orchestrator, a new node type's executor only ever implements `ExecuteAsync` — it never re-implements approval, retry, timeout, idempotency, or compensation logic (constitution §2.II OCP).
