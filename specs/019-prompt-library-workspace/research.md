# Phase 0 Research: Prompt Library & Prompt Engineering Workspace

All findings below are grounded in a direct read of the existing Ask Lucy solution (not assumption),
cross-referenced against `spec.md`'s clarified requirements and the constitution. Where a design
choice extends an existing, already-established pattern, the source file is cited. There are no
`NEEDS CLARIFICATION` markers in `spec.md`'s Technical Context — every item below resolves a design
choice the spec deliberately left to planning (it is business-requirement-complete, not
technology-silent by omission).

---

## Decision 1 — New bounded context: `Prompts`

**Decision**: Introduce `Prompts` as a new bounded context (`Domain/Prompts`, `Application/Prompts`,
`Infrastructure/Prompts`, `Persistence/Configurations/Prompts`) — not folded into `Chats`,
`KnowledgeBases`, or `Ai`.

**Rationale**: A Prompt has a distinct reason to change (constitution §2.II SRP) from all three
candidate hosts — it is authored, versioned, and tested independently of any conversation (spec.md's
entire premise: "a prompt is a reusable product asset ... independently from individual
conversations"), it is not a knowledge artifact, and `Ai` is already a large bounded context for
provider/model administration and chat-turn orchestration, not prompt authoring. This mirrors the
exact precedent `KnowledgeBases` set relative to `Retrieval` and `Memory` set relative to `Chats`
(specs/016, specs/018): a cross-cutting, independently-lifecycled concept gets its own bounded
context even though other contexts consume it.

**Alternatives considered**: (a) Fold into `Ai` — rejected, `Ai` already spans provider
administration, chat-turn handling, voice, and translation; adding prompt CRUD/versioning/testing
would make it a god-context (constitution §2.II). (b) Fold into `Chats` — rejected, prompts explicitly
outlive and exist independently of any conversation (spec.md User Story 1's entire premise).

---

## Decision 2 — Prompt execution reuses `IAIProvider`/`IAIProviderResolver` verbatim; streaming via MediatR `IStreamRequest` + SSE

**Decision**: `ExecutePromptCommand : IStreamRequest<PromptStreamChunk>`, handled by
`ExecutePromptCommandHandler : IStreamRequestHandler<ExecutePromptCommand, PromptStreamChunk>`,
resolves the requested provider via the existing `IAIProviderResolver` and calls
`IAIProvider.StreamChatAsync(messages, model, parameters, ct)` — the exact model/parameter-aware
overload `specs/005-multi-provider-ai-engine` already added to `IAIProvider`
(`src/AskLucy.Application/Abstractions/IAIProvider.cs`). The Web endpoint sets
`Response.ContentType = "text/event-stream"` and does `await foreach (var chunk in
mediator.CreateStream(...))`, the identical shape `AiController.cs` already uses for
`StreamVoiceReplyCommand` (`src/AskLucy.Web/Controllers/v1/AiController.cs:62-70`).

**Rationale**: FR-046 requires the prompt system "MUST NOT call any AI provider directly or embed
provider-specific logic" — the existing abstraction is reused exactly as-is, zero new provider
integration surface. Streaming (FR-041) reuses a pattern this codebase has already proven twice
(chat streaming, voice-reply streaming) rather than introducing a new streaming mechanism.

**Alternatives considered**: A dedicated prompt-execution SignalR hub (like `MemoryHub`) — rejected;
SignalR is used elsewhere for *server-initiated* async notifications (approval events, conflict
resolution), not for a synchronous request-driven token stream, which SSE already serves correctly
for chat and voice.

---

## Decision 3 — RAG and Memory context reuse `IRagService`/`IMemoryService` verbatim, with zero interface changes

**Decision**: When a prompt requests RAG context (FR-081), `ExecutePromptCommandHandler` calls
`IRagService.RetrieveContextAsync(promptExecutionId, resolvedQuery, knowledgeBaseIds, ct)` — passing
the new `PromptExecution.Id` in the `userChatId` parameter slot. When a prompt requests Memory
context (FR-082), it calls `IMemoryService.RetrieveRelevantMemoriesAsync(userId, promptExecutionId,
projectId: null, resolvedQuery, ct)`, same substitution.

**Rationale**: Reading both implementations directly
(`src/AskLucy.Application/Retrieval/RagService.cs:27-45`,
`src/AskLucy.Application/Memory/MemoryService.cs:31,83`) confirms `userChatId` is used **only** as a
structured-logging correlation id in both services — never as a foreign key, never dereferenced
against `Chats.UserChat`. Passing `PromptExecution.Id` in that slot is a semantically honest reuse
(it is still "the id of the thing this retrieval call happened on behalf of") that requires **zero**
signature changes to either interface, directly satisfying FR-081/FR-082's "MUST NOT implement a
separate or duplicate retrieval/memory mechanism" and the constitution's Infrastructure-isolation
principle (§3) at the lowest possible cost. `PromptExecutionResult` stores its own copy of the
returned context/citations (see data-model.md) for the prompt workspace's own observability — it does
**not** write to `Citation`/`MemoryReference`, which are FK'd to a real `Chats.Message`/`UserChat` and
would misrepresent a standalone test execution as chat history.

**Alternatives considered**: Adding a `Guid?`-typed overload to both interfaces — rejected, unneeded
once the correlation-id-only usage was confirmed by reading the implementation; would be speculative
signature churn on two interfaces already depended on by `SendChatMessageCommandHandler` for no
behavioral benefit (constitution §2.III YAGNI).

---

## Decision 4 — Inserting a prompt into a conversation (FR-080) delegates to the existing chat send path; no new AI-call logic

**Decision**: `InsertPromptIntoConversationCommand` resolves the prompt's variables and structural
components into a single composed message string (system instructions are merged into the
conversation's existing system-prompt handling exactly as `SendChatMessageCommandHandler` already
does; user instructions become the new user message text), then delegates to the **existing**
`SendChatMessageCommand` for everything else — provider/model selection, RAG, memory, streaming,
persistence.

**Rationale**: FR-080 requires the conversation's "existing provider/model selection and prior
message context" be preserved — the existing `SendChatMessageCommandHandler` already owns that
exact contract. Re-deriving it inside `Prompts` would duplicate a non-trivial, already-tested handler
(constitution §2.III DRY) and risk the two paths drifting (e.g., a future RAG/Memory change applied
to one but not the other). Only prompt-specific work — variable resolution and validation
(FR-013/FR-014), name/compatibility checks (FR-004) — belongs in `Prompts`; message delivery stays a
single, already-correct code path.

**Alternatives considered**: A parallel `SendPromptMessageCommand` reimplementing send-message
end-to-end — rejected as exactly the duplication FR-080/constitution §2.III forbid.

---

## Decision 5 — `PromptFolder` reuses `KnowledgeBaseFolder`'s nested-hierarchy pattern exactly

**Decision**: `PromptFolder` carries `ParentFolderId` (nullable self-FK) and a stored `Depth`
computed at create/move time (not recomputed per-read), with a `MaxNestingDepth` constant enforced in
the same two places `KnowledgeBaseFolder` enforces it: `Create` and `MoveTo`
(`src/AskLucy.Domain/KnowledgeBases/KnowledgeBaseFolder.cs:25-74`). A move is rejected
(`DomainRuleViolationException`) if the target parent is the folder itself or any of its own
descendants (cycle prevention, spec.md Edge Cases) — computed by walking `ParentFolderId` up from the
proposed new parent and checking whether the folder being moved appears in that chain, the same
cycle-check shape `MoveFolderCommandHandler` already implements for knowledge bases.

**Rationale**: Clarification session 2026-08-10 confirmed folders must support nested sub-folders to
an arbitrary depth (spec.md FR-054) — this codebase already has exactly one working nested-folder
implementation. Reusing its field shape, depth-tracking strategy, and cycle-prevention algorithm is
Convention over Configuration (constitution §2.VII), not a new folder mechanism to design, review, and
test from scratch.

**Alternatives considered**: A materialized path (`/root/child/grandchild`) — rejected; the existing
`ParentFolderId` + stored `Depth` approach is already proven in this codebase and a path-string scheme
would be a second, inconsistent nested-hierarchy convention for no benefit spec.md asks for.

---

## Decision 6 — `PromptCategory`/`PromptTag` reuse `KnowledgeBaseCategory`/`KnowledgeBaseTag`'s shape exactly

**Decision**: `PromptCategory.OwnerId` is nullable — `null` means predefined and platform-shared (a
small seeded set, e.g. mirroring spec.md's Prompt Types list), non-null means custom and private to
that owner, identical to `KnowledgeBaseCategory`
(`src/AskLucy.Domain/KnowledgeBases/KnowledgeBaseCategory.cs`). `PromptTag` is a per-prompt value row
(`PromptId`, `OwnerId`, `Value`) with no separate deduplicated tag-catalog table, identical to
`KnowledgeBaseTag` (`src/AskLucy.Domain/KnowledgeBases/KnowledgeBaseTag.cs`) — a tag carries no
attributes beyond its text, so a master table would add a join with no behavior it enables.

**Rationale**: Convention over Configuration (constitution §2.VII) — this codebase has already solved
"predefined-vs-custom classification" and "free-form label" exactly once each; spec.md's Category/Tag
requirements (FR-050, FR-052) map onto both without modification.

**Alternatives considered**: None seriously — the existing shapes are a direct, unmodified fit.

---

## Decision 7 — Name uniqueness (FR-006) is an application-layer pre-check plus a defense-in-depth filtered unique index

**Decision**: `CreatePromptCommandHandler`/`ImportPromptsCommandHandler`/`DuplicatePromptCommandHandler`
query for an existing, non-deleted prompt with the same owner and case-insensitive name before
inserting, and throw `DuplicateResourceException` (→ 409, already wired in
`ProblemDetailsMiddleware.cs:119-123`) when one exists — the exact pattern
`CreateCustomCategoryCommandHandler`
(`src/AskLucy.Application/KnowledgeBases/Commands/CreateCustomCategory/CreateCustomCategoryCommandHandler.cs:19`)
already uses. A filtered unique index (`OwnerId`, lower-invariant `Name`) `WHERE IsDeleted = 0` is
added as defense-in-depth against the narrow concurrent-create race, translated by EF Core's own
unique-constraint-violation path (not a new middleware branch — a rare double-submit race is
acceptable to surface as the existing generic conflict path).

**Rationale**: Directly implements the clarification session's "unique per user" answer using this
codebase's one already-proven uniqueness-enforcement pattern rather than inventing a second one.

**Alternatives considered**: Enforcing uniqueness only via the database index (no pre-check) —
rejected; every existing instance of this rule in the codebase pre-checks for a clean, specific
`DuplicateResourceException` message rather than surfacing a raw constraint-violation 409.

---

## Decision 8 — Concurrent-edit rejection (FR-007) needs zero new code — `BaseEntity.RowVersion` + `DbUpdateConcurrencyException` already do this

**Decision**: `Prompt` (and every other new aggregate) extends `BaseEntity`, which already carries
`RowVersion` (EF Core `IsRowVersion()` concurrency token) — no additional field or check is written.
A second concurrent `UpdatePromptCommand` against a stale `RowVersion` throws
`DbUpdateConcurrencyException`, already mapped by `ProblemDetailsMiddleware.cs:107-111` to `409
concurrency-conflict` with "This item was modified by another request. Please reload and try again."

**Rationale**: The clarification session's answer ("reject with conflict error, optimistic
concurrency, no auto-merge") is *exactly* this codebase's existing, universal concurrency contract —
confirmed by reading `BaseEntity.cs` and the middleware together. This is not a design decision so
much as a recognition that the requirement is already satisfied platform-wide the moment `Prompt`
extends `BaseEntity` like every other aggregate.

**Alternatives considered**: None — a bespoke per-field conflict-detection scheme would contradict
constitution §5's existing concurrency-token mandate for no benefit.

---

## Decision 9 — Versioning: immutable `PromptVersion` snapshots, mirrors `MemoryVersion`'s append-only convention

**Decision**: `PromptVersion` rows are created only via an internal `Prompt.CreateVersionSnapshot()`
helper invoked from `Prompt.ApplyEdit(...)` — never constructed directly by Application-layer code —
and carry no update/delete methods (append-only), mirroring `MemoryVersion`
(`src/AskLucy.Domain/Memory/MemoryVersion.cs` pattern, data-model.md precedent from specs/018).
`Prompt.CurrentVersionId` always points at the latest row; `RestoreVersionCommand` calls
`Prompt.RestoreFrom(version)`, which itself creates a **new** version snapshot copying the restored
content (FR-033 — restoring never deletes intervening history).

**Rationale**: Directly satisfies FR-030–FR-033 using a pattern this codebase has already implemented
once for an analogous "every edit is an immutable, restorable snapshot" requirement.

**Alternatives considered**: Temporal tables (SQL Server system-versioning) — rejected; `MemoryVersion`
already established the manual-snapshot convention for exactly this kind of requirement, and temporal
tables would be a second, inconsistent versioning mechanism plus loss of the explicit
`ChangeDescription`/author metadata FR-031 requires as first-class queryable fields.

**FR-020 note**: A `Prompt` *is* the reusable template spec.md FR-020 describes — there is no
separate template entity. Executing a prompt (`ExecutePromptCommand`, tasks.md T060) only reads
`Prompt`/`PromptVersion`/`PromptVariable`; it never calls `ApplyEdit`/`CreateVersionSnapshot`, so
repeated execution with different variable values cannot mutate the saved prompt. Verified by
tasks.md T037's assertion that only `ApplyEdit` creates a new version.

---

## Decision 10 — Variable placeholder detection is a pure Domain regex helper; no template-engine dependency

**Decision**: `{{variable_name}}` placeholders are detected via a single compiled regex
(`\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}`) in a pure, I/O-free Domain helper
(`PromptContentAnalyzer` — static, no dependencies), used both to auto-detect variables on save
(FR-010) and to flag undeclared/unused variables before execution (FR-014). Resolution at execution
time is a literal string-replace pass over the same placeholder set, run **after** all variable
validation (FR-013) passes.

**Rationale**: The full variable syntax needed (name capture, no nesting, no expressions/conditionals
— spec.md never asks for either) is satisfied by a regex; pulling in a templating library (Handlebars,
Scriban, etc.) would be an unjustified new dependency for a feature this simple (constitution §2.III
YAGNI, "avoid unnecessary dependencies").

**Alternatives considered**: A general-purpose templating engine — rejected, no requirement in spec.md
calls for loops/conditionals/helpers; a regex satisfies every FR-010–FR-014 requirement with zero new
NuGet packages.

---

## Decision 11 — Estimated cost reuses `CostEstimator.Estimate(...)` verbatim

**Decision**: `PromptExecutionResult`'s `EstimatedCostUsd` is computed via the existing static
`CostEstimator.Estimate(ModelPricing?, int? inputTokens, int? outputTokens)`
(`src/AskLucy.Application/Ai/CostEstimator.cs`) — same nullable-propagation contract (returns `null`,
never a fabricated zero, when pricing is unavailable).

**Rationale**: FR-042 requires displaying estimated cost; this codebase already has one correct,
tested cost-estimation function used by chat/comparison flows. No new pricing logic is introduced.

**Alternatives considered**: None — direct reuse, no adaptation needed.

---

## Decision 12 — Search reuses SQL Server full-text search, matching the conversation-search precedent

**Decision**: A `FULLTEXT INDEX` covering `Prompt.Name`, `Prompt.Description`,
`Prompt.SystemInstructions`, `Prompt.UserInstructions` (multi-column, one index, SQL Server native
capability) backs free-text search (FR-052). Tag/category/variable-name/author filters are ordinary
indexed equality/join predicates (short discrete values — full-text search adds nothing for these).
List/search queries are cursor-paginated (constitution §6), matching `KnowledgeBases`/`Documents`/
`Retrieval`/`Chats`.

**Rationale**: `specs/002-chat-history-management` already added exactly this mechanism for
`UserChats.Title`/`Messages.Content`
(`src/AskLucy.Persistence/Migrations/20260729190610_AddConversationFullTextSearch.cs`) — reusing the
identical `CREATE FULLTEXT CATALOG`/`CREATE FULLTEXT INDEX` migration shape satisfies SC-003 (locate a
prompt among 1,000+ in under 10 seconds) and FR-053 (thousands of prompts, no full-table scan) without
introducing a new search technology (e.g., a dedicated search-engine dependency), which spec.md never
asks for and constitution §2.III (YAGNI) would flag as premature at this scale.

**Alternatives considered**: A dedicated external search index (Elasticsearch/Azure AI Search) —
rejected; the existing SQL Server full-text mechanism already meets SC-003/FR-053 at the stated
per-user scale (thousands, not millions, of rows) with zero new infrastructure.

---

## Decision 13 — Bulk export/import: one JSON file, top-level array, atomic all-or-nothing validation, explicit schema version

**Decision**: An export file is `{ "schemaVersion": 1, "prompts": [ <per-prompt export object>, ... ] }`
— a single-prompt export is simply a one-element array, so the single and bulk cases share one file
shape and one validator (no second "bundle" schema). `ImportPromptsCommandHandler` deserializes and
validates **every** entry (structure, required fields, variable/type well-formedness) before creating
any row; if any entry fails, the whole import is rejected and nothing is persisted (FR-071, matching
the clarification session's bulk-export answer combined with the pre-existing "create nothing on
failure" requirement).

**Rationale**: A single schema for both cases avoids a parallel "bundle vs. single" contract
(constitution §2.III DRY) and keeps FR-070–FR-072 satisfied with one validator, one Application
command, one contract document. `schemaVersion` gives forward compatibility for a future export-format
change without breaking older exported files (spec.md Edge Case: "an unrecognized/corrupted/wrong
version of the export format" is rejected outright).

**Alternatives considered**: Separate `ExportPromptCommand`/`ExportPromptsBundleCommand` with distinct
file shapes — rejected, unnecessary duplication once a one-element array already covers the
single-prompt case.

---

## Decision 14 — Prompt content assembly enforces instruction priority via ordered, explicitly-delimited message construction (no runtime enforcement mechanism needed)

**Decision**: The message list sent to `IAIProvider` is built in a fixed, explicit order: (1) the
prompt's own system + developer instructions as a `ChatRole.System` message, (2) memory context (when
requested) as a second `ChatRole.System` message delimited `<user_memory>...</user_memory>` — reusing
the exact delimiter/defensive-framing convention `specs/018-ai-memory-system` established
(research.md Decision 2/9 there), (3) RAG context (when requested) as a third `ChatRole.System`
message delimited `<retrieved_context>...</retrieved_context>`, matching `RagService`'s existing
framing, (4) the resolved user instructions (with variables substituted) as the final `ChatRole.User`
message. Variable values and retrieved content are **only ever** interpolated into the System/User
message *content strings* — never concatenated into, or allowed to replace, the instruction segments
themselves.

**Rationale**: FR-083/FR-092 (prompt-injection considerations) require system instructions,
developer instructions, template, variables, RAG context, and memory context stay structurally
distinguishable and that none of the latter can override the former. This is exactly the ordering and
delimiter discipline `specs/018`'s memory-injection design already proved correct for an identical
constraint (constitution §8's prompt-injection clause) — reused here rather than re-derived.

**Alternatives considered**: A single flattened prompt string — rejected outright, directly
contradicts FR-002/FR-083's explicit structural-separation requirement.

---

## Summary of dependencies

**Zero new NuGet packages and zero new frontend dependencies.** Every execution, retrieval, cost,
folder, category/tag, search, versioning, and error-handling concern reuses an existing abstraction,
pattern, or piece of infrastructure already present in this solution. The only genuinely new
Application-owned abstraction introduced by this feature is `PromptContentAnalyzer` (Decision 10) —
a pure, dependency-free Domain helper, not a new external dependency.
