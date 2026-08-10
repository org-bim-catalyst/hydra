# Contract: `IAgentTool` and the Built-In Tool Catalog

**Feature**: [../spec.md](../spec.md) | **Research**: [../research.md](../research.md) (Decision 10)

## Interface

`src/AskLucy.Application/Agents/Tools/IAgentTool.cs`:

```csharp
public interface IAgentTool
{
    string Name { get; }                              // matches AgentTool.ToolName (data-model.md)
    string Description { get; }                        // shown to the model in the planning prompt (research.md Decision 11)
    AgentToolRiskLevel RiskLevel { get; }               // Low | Medium | High | Critical (FR-020)
    IReadOnlyList<AgentToolPermission> RequiredPermissions { get; }
    JsonSchema InputSchema { get; }                     // FR-020/FR-021
    JsonSchema OutputSchema { get; }

    Task<AgentToolResult> ExecuteAsync(
        AgentToolExecutionContext context, JsonDocument input, CancellationToken cancellationToken);
}

public sealed record AgentToolExecutionContext(
    Guid ExecutionId, Guid StepId, string UserId, Guid AgentId, Guid AgentVersionId);

public sealed record AgentToolResult(bool Succeeded, JsonDocument? Output, string? FailureReason);
```

`AgentToolPermission` (FR-022/FR-023): `ReadKnowledge | ReadMemory | ReadFile | WriteFile | ExternalNetwork | SendEmail | ExecuteCode | ModifyData | HighRiskOperation` — matches the spec's own list verbatim.

## Runtime contract (enforced by the Agent Runtime, not by each tool)

1. **Input validation** (FR-021): the runtime validates `input` against `InputSchema` *before* calling `ExecuteAsync`; a schema violation never reaches tool code — it is recorded as an `AgentToolCall` failure (`AgentExecutionErrorCategory.InvalidToolOutput`... actually the input side — see data-model.md's `AgentExecutionErrorCategory`, which covers both directions) without invoking the tool.
2. **Permission check** (FR-022/FR-023): the runtime resolves `RequiredPermissions` against the *executing user's* actual permissions (via the same guards/repositories each tool wraps — e.g. `IKnowledgeBaseRepository.ResolveOwnedIdsAsync` for `ReadKnowledge`) before every call, every time — never cached across steps, since access can change mid-execution (edge case in spec.md).
3. **Approval gate** (FR-025): if `RiskLevel` is `High` or `Critical` and no matching, enabled `AgentPolicy` covers this exact call, the runtime creates a `Pending` `AgentApproval` and suspends the step (`WaitingForApproval`) instead of calling `ExecuteAsync`.
4. **Output validation** (FR-021): the runtime validates the tool's `Output` against `OutputSchema` after the call returns; a violation is recorded as a failure even if the tool itself reported `Succeeded: true`.
5. **Duplicate-call detection** (FR-039): the runtime compares `(ToolName, ValidatedInputJson)` against every prior `AgentToolCall` in the same execution before dispatching; an exact repeat halts the execution rather than calling the tool again.

Because all five of these live in the runtime, not in each `IAgentTool` implementation, a new built-in tool only ever needs to implement `ExecuteAsync` plus its schema/metadata — it never re-implements validation, permission-checking, or approval logic (constitution §2.II OCP: new tool = new class, zero edits to existing runtime code).

## Built-in tool catalog (FR-024)

| Tool | Wraps | RiskLevel | Permissions |
|---|---|---|---|
| `ConversationTool` | `IUserChatRepository`/`IMessageRepository` (read-only: recent messages in the linked conversation) | Low | (none — read-only, same conversation the execution is already linked to) |
| `KnowledgeSearchTool` | `IRagService.RetrieveContextAsync` (research.md Decision 4), scoped via `IKnowledgeBaseRepository.ResolveOwnedIdsAsync` | Low | `ReadKnowledge` |
| `DocumentSearchTool` | `IDocumentRepository.SearchAsync` + `DocumentOwnershipGuard` | Low | `ReadFile` |
| `MemorySearchTool` | `IMemoryService.RetrieveRelevantMemoriesAsync` (research.md Decision 4) | Low | `ReadMemory` |
| `MemoryWriteTool` | `CreateMemoryCandidateCommand` via `ISender` (research.md Decision 5) | **Medium** | `ModifyData` |
| `PromptExecutionTool` | `ExecutePromptCommand` via `ISender` (research.md Decision 6) | Low | (none beyond the prompt's own ownership check) |
| `FileReadTool` | `IDocumentRepository` + `DocumentOwnershipGuard` + `IFileStorage.OpenReadAsync` (research.md Decision 7) | Low | `ReadFile` |
| `FileMetadataTool` | `IDocumentRepository` + `DocumentOwnershipGuard` (metadata only, no content read) | Low | `ReadFile` |

No built-in tool in this release is `High`/`Critical` risk or requires `WriteFile`/`ExternalNetwork`/`SendEmail`/`ExecuteCode`/`HighRiskOperation` — those permission values and risk levels exist in the contract so future tools (web search, email, code execution — explicitly deferred per spec's "Future" tool list) can declare them without a contract change, and so the approval-gate machinery (item 3 above) has real cases to exercise in tests even before those tools ship (a test-only `FakeHighRiskTool` fixture covers this — see `tests/AskLucy.Application.Tests/Agents/AgentApprovalWorkflowTests.cs` in the testing plan).
