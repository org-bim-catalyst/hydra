# Quickstart: Validating the AI Memory System

**Feature**: [spec.md](./spec.md) | **Data model**: [data-model.md](./data-model.md) |
**Contracts**: [contracts/](./contracts/)

Manual/scripted validation scenarios proving the feature works end-to-end, mapped to the spec's
user stories and success criteria. Run after implementation, before marking the feature done
(constitution §19 Definition of Done).

## Prerequisites

- Solution built and running locally (`dotnet run` for `AskLucy.Web`), against a SQL Server
  instance that supports the native `vector` type (SQL Server 2025+/Azure SQL, same requirement
  specs/016 already established), with this feature's migration applied (new `Memory`/`Projects`
  tables plus the additive `UserChat.ProjectId` column).
- A logged-in test user with `MemoryPreference.MemoryEnabled = true` and all categories at the
  default `Automatic` approval mode, and a second, separate user account to validate ownership
  scoping (FR-027).
- Hangfire dashboard/server running so `MemoryExtractionJob` and `MemoryExtractionSweepJob` process
  (research.md Decision 6).
- Ability to simulate the memory subsystem being unreachable (e.g., point the memory embedding
  provider or `SqlServerMemoryVectorStore` at an invalid endpoint/connection) for the
  degraded-mode scenario.

## Scenario 1 — Lucy remembers me across conversations (User Story 1 / SC-001, SC-006)

1. In conversation A, state a stable preference or fact (e.g., "I prefer TypeScript over
   JavaScript"). Wait for the background extraction pass (per-turn enqueue, research.md Decision 6)
   to run — confirm a new `Memory` row appears with `State = Active` (default `Automatic` approval
   mode) via `GET /api/v1/memories`.
2. Start a brand-new conversation B and ask a question where that preference is relevant. Confirm
   Lucy's response reflects the fact without it being restated (US1 AC1), and confirm no
   perceptible delay was added to the start of the response (SC-006).
3. Confirm `GET /api/v1/chats/{chatId}/messages/{messageId}/memory-references` for that response
   lists the memory used (FR-014).
4. Disable memory (`PUT /api/v1/memories/preferences`, `memoryEnabled: false`). State a new fact,
   then start another new conversation and confirm nothing from the disabled period is referenced
   (US1 AC2). Re-enable afterward.
5. **Degraded-mode check**: simulate the memory subsystem being unavailable (see Prerequisites),
   then send a message in a conversation with existing relevant memories. Confirm the response still
   generates, without memory context, with no added delay, and the failure is present in structured
   logs (not silent) — clarified 2026-08-09, FR-014a. Restore the subsystem afterward.

**Pass condition**: matches spec.md User Story 1's three acceptance scenarios.

## Scenario 2 — Memory Center: review and manage what Lucy remembers (User Story 2 / SC-002)

1. Open the Memory Center (`GET /api/v1/memories`); confirm every memory shows content, category,
   source, creation date, and lifecycle state (FR-017, US2 AC1).
2. Edit a memory's content (`PUT /api/v1/memories/{id}`); confirm the next relevant conversation
   uses the corrected text and `GET /api/v1/memories/{id}` shows the prior value in `history` (US2
   AC2).
3. Delete a memory (`DELETE /api/v1/memories/{id}`); confirm it no longer appears in the list and is
   never used again (US2 AC3).
4. With several memories present, search/filter by category and by free-text query; confirm only
   matching memories return (US2 AC4).
5. Time the full find → act cycle (open Memory Center → locate a specific memory → edit/delete it)
   — should complete in under 30 seconds (SC-002).

**Pass condition**: matches spec.md User Story 2's four acceptance scenarios.

## Scenario 3 — Approval workflow (User Story 3 / SC-004)

1. Set a category's approval mode to `Manual` (`PUT /api/v1/memories/preferences`). State a fact in
   that category; confirm the resulting memory is `State: PendingApproval` and not used in any
   conversation until approved (US3 AC1).
2. Approve it (`POST /api/v1/memories/{id}/actions/approve`); confirm it becomes `Active` and is now
   eligible for retrieval (US3 AC2).
3. Repeat with a rejection (`POST /api/v1/memories/{id}/actions/reject`); confirm it is discarded and
   never used (US3 AC3).
4. Set a category back to `Automatic`; state a fact; confirm it becomes `Active` without manual
   approval and still appears in the Memory Center with its source disclosed (US3 AC4).
5. Set a category to `Disabled`; confirm no new candidates are created for that category at all
   (US3 AC5).
6. State something plausibly sensitive (e.g., a health-related detail); confirm it is held for
   manual review regardless of the category's `Automatic` setting (FR-008), and that at least 95% of
   a batch of clearly-sensitive test statements are correctly flagged (SC-004).

**Pass condition**: matches spec.md User Story 3's five acceptance scenarios.

## Scenario 4 — Account-level privacy controls (User Story 4 / SC-003, SC-005)

1. With memories present, disable memory entirely; confirm no new memories are created and none are
   used, while existing rows remain in storage (not deleted) (US4 AC1).
2. Clear all memories (`POST /api/v1/memories/actions/clear-all`, `confirm: true`); confirm every
   memory is permanently gone (US4 AC2). Confirm the disable → clear-all round trip took three or
   fewer user actions (SC-003).
3. With memories present again, export (`POST /api/v1/memories/actions/export` →
   `GET /api/v1/memories/exports/{id}`); confirm a complete, human-readable file downloads (US4 AC3).
   Repeat against an account with zero memories; confirm a valid, empty export, not an error.
4. Disable one category only; confirm no memories of that category are created or used going
   forward, while other enabled categories keep working (US4 AC4).
5. **Cross-user check**: as the second test user, attempt to read/modify the first user's memory by
   id; confirm `404` (not `403`) and zero visibility (FR-027, SC-005).

**Pass condition**: matches spec.md User Story 4's four acceptance scenarios; zero cross-user memory
exposure.

## Scenario 5 — Project-scoped memory (User Story 5)

1. Create a Project (`POST /api/v1/projects`) and assign a conversation to it
   (`PUT /api/v1/chats/{chatId}/project`). State a fact relevant only to that Project.
2. Start another conversation inside the same Project; confirm the fact is available (US5 AC1a).
3. Start a conversation outside the Project (or in a different Project); confirm the fact is **not**
   used (US5 AC1b).
4. In a conversation with no Project assigned, confirm only general (non-project-scoped) memories
   are considered (US5 AC2).
5. Delete the Project; confirm its scoped memories move to `State: Archived` (not deleted) and
   remain visible/exportable from the Memory Center outside the Project context (US5 AC3).

**Pass condition**: matches spec.md User Story 5's three acceptance scenarios.

## Scenario 6 — Conflict detection and asynchronous resolution (User Story 6)

1. State a fact (e.g., "I use Angular"). Later, state a directly contradicting fact ("I moved to
   React"). Confirm the memory updates to the new information and the prior value is visible in
   `history` (US6 AC1, FR-015).
2. Construct an ambiguous case (a statement that could supersede *or* merely supplement an existing
   memory). Confirm: the live conversation turn completes normally with no interruption (clarified
   2026-08-09, Q2); a `MemoryNotification` (`ConflictNeedsConfirmation`) appears; the ambiguous
   memory is excluded from retrieval until resolved
   (`POST /api/v1/memories/{id}/actions/resolve-conflict`) (US6 AC2, FR-016).
3. Confirm the resolved memory's history shows when the conflict was detected and resolved (US6
   AC3).

**Pass condition**: matches spec.md User Story 6's three acceptance scenarios.

## Scenario 7 — Background extraction resilience (FR-006b)

1. Temporarily make the extraction LLM call fail (e.g., point the resolved "utility model" provider
   at an invalid key). Trigger a conversation turn; confirm `MemoryExtractionJob` retries
   automatically (Hangfire dashboard shows retry attempts, research.md Decision 6) and, once
   retries are exhausted, the failure is present in structured logs — with **no** user-facing error
   surfaced for that specific pass (FR-006b).
2. Restore the provider; confirm subsequent turns extract normally again.

**Pass condition**: matches FR-006b — automatic retry with backoff, team-observable failure, no
user-facing noise for background-job failures.
