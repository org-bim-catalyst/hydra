# Quickstart: Validating the Prompt Library & Prompt Engineering Workspace

**Feature**: [spec.md](./spec.md) | **Data model**: [data-model.md](./data-model.md) |
**Contracts**: [contracts/](./contracts/)

Manual/scripted validation scenarios proving the feature works end-to-end, mapped to the spec's
user stories and success criteria. Run after implementation, before marking the feature done
(constitution §19 Definition of Done).

## Prerequisites

- Solution built and running locally (`dotnet run` for `AskLucy.Web`), against a SQL Server instance
  with this feature's migration applied (new `Prompts` bounded-context tables, plus the
  `FULLTEXT CATALOG`/`FULLTEXT INDEX` on `Prompt`, research.md Decision 12).
- At least one enabled AI provider/model configured (specs/005-multi-provider-ai-engine), including
  one model that supports vision or JSON mode and one that does not, to exercise capability gating
  (FR-004).
- A logged-in test user, and a second, separate user account to validate ownership scoping (FR-060,
  FR-090, SC-008).
- At least one existing Knowledge Base with indexed content (specs/016) to exercise RAG-context
  prompts (FR-081), and at least one stored memory (specs/018) to exercise memory-context prompts
  (FR-082).
- Two browser sessions/tabs logged in as the same user, to exercise the concurrent-edit scenario
  (FR-007).

## Scenario 1 — Create and reuse a structured prompt (User Story 1 / SC-001)

1. `POST /api/v1/prompts` with a system instruction, a user instruction containing
   `{{document}}`/`{{target_language}}`/`{{summary_length}}`, and matching variable definitions
   (FR-001–FR-005, FR-010–FR-012). Confirm `201 Created` and that the automatically-detected variable
   list matches the placeholders in the content.
2. `GET /api/v1/prompts/{id}` — confirm every field (system/user instructions, variables, category,
   tags) matches what was saved (US1 AC2).
3. Time the full create-to-saved flow — should complete in under 3 minutes (SC-001).
4. As the second test user, `GET /api/v1/prompts` and `GET /api/v1/prompts/{id}` (the first user's
   prompt id) — confirm the list is empty and the direct `GET` returns `404` (US1 AC4, SC-008).
5. Attempt to create a second prompt for the first user with the exact same name (any case) — confirm
   `409 Conflict` (`duplicate-resource`) (FR-006, spec.md Edge Cases).

**Pass condition**: matches spec.md User Story 1's four acceptance scenarios.

## Scenario 2 — Test a prompt before relying on it (User Story 2 / SC-002, SC-004, SC-010)

1. Open the saved prompt from Scenario 1 and `POST /api/v1/prompts/{id}/executions` leaving the
   required `document` variable blank — confirm `400 Bad Request` (`validation-failed`) identifying
   the missing variable, and that **no** provider call occurred (no new `PromptExecution` with
   `Outcome: Success`) (US2 AC1, SC-004).
2. Supply all required variables, a provider, and a model; execute — confirm the SSE stream delivers
   content deltas and a final chunk with token usage, and that
   `GET /api/v1/prompts/{id}/executions/{executionId}` shows provider, model, latency, token usage,
   and estimated cost (US2 AC2). Time open-prompt-to-first-stream-content — under 60 seconds (SC-002).
3. `POST /api/v1/prompts/{id}/test-cases` from that execution — confirm it is retrievable via
   `GET /api/v1/prompts/{id}/test-cases` (US2 AC3).
4. Attempt to execute the same prompt against a model that lacks a capability the prompt declares as
   required (e.g., set `requiredCapabilities.jsonMode = true`, target a non-JSON-mode model) — confirm
   `400 Bad Request` (`domain-rule-violation`) before any provider call (US2 AC4).
5. **Failure path**: point the selected provider at an invalid/unreachable configuration temporarily,
   execute, and confirm the SSE stream ends with an explicit error event (never a silently truncated
   stream) and the persisted `PromptExecution.Outcome = Failed` — never presented as a successful,
   empty result (SC-010). Restore the provider configuration afterward.

**Pass condition**: matches spec.md User Story 2's four acceptance scenarios.

## Scenario 3 — Version, compare, and restore (User Story 3 / SC-005)

1. Edit the prompt's content twice (`PUT /api/v1/prompts/{id}` twice with different
   `userInstructions`) — confirm `GET /api/v1/prompts/{id}/versions` lists three versions (original +
   two edits), each with its own content preserved (US3 AC1).
2. `GET /api/v1/prompts/{id}/versions/compare?from=1&to=3` — confirm the diff correctly surfaces the
   content/variable/model-setting differences (US3 AC2).
3. `POST /api/v1/prompts/{id}/versions/1/actions/restore` — confirm
   `GET /api/v1/prompts/{id}` now matches version 1's content, and
   `GET /api/v1/prompts/{id}/versions` still lists **four** versions (the restore itself became a new
   version 4) — no version was deleted or overwritten (US3 AC3, SC-005).
4. `POST /api/v1/prompts/{id}/versions/2/actions/duplicate` — confirm a brand-new, independent
   `Prompt` is created seeded from version 2's content, with its own version-1 history (US3 AC4).

**Pass condition**: matches spec.md User Story 3's four acceptance scenarios; zero destroyed version
history across the whole scenario (SC-005).

## Scenario 4 — Organize and find prompts at scale (User Story 4 / SC-003)

1. Create prompts across at least three categories/tags, place some in nested folders (create a
   folder, create a sub-folder inside it via `POST /api/v1/prompt-folders` with `parentFolderId` set —
   FR-054), favorite and pin a subset.
2. `GET /api/v1/prompts?query=<keyword>` — confirm matches on name/description/content/variable name
   return, ranked best-match-first (US4 AC1).
3. `GET /api/v1/prompts?categoryId=&tag=&folderId=` (combined) — confirm only prompts matching **all**
   applied filters return (US4 AC2).
4. Toggle favorite/pinned on a prompt; confirm it appears/disappears from
   `GET /api/v1/prompts?favorite=true` / `?pinned=true` accordingly, with the prompt itself unaffected
   (US4 AC3).
5. Execute a couple of prompts successfully; confirm `?view=recentlyUsed` reflects successful-only
   executions in the correct order (a deliberately **failed** execution must not move a prompt up in
   this view — spec.md Clarifications) (US4 AC4).
6. Seed ~1,000+ prompts (script/import) for one test user; time a search and a filtered list call —
   both under 10 seconds (SC-003, FR-053).
7. Attempt to `PUT /api/v1/prompt-folders/{id}/move` a folder into one of its own descendants —
   confirm `400 Bad Request` (cycle rejected, spec.md Edge Cases).

**Pass condition**: matches spec.md User Story 4's five acceptance scenarios; SC-003 timing holds at
1,000+ prompts.

## Scenario 5 — Use a saved prompt inside a live conversation (User Story 5 / SC-006)

1. In an existing conversation with prior messages, `POST /api/v1/chats/{chatId}/prompt-messages`
   with a saved prompt id and a subset of variable values — confirm any variable not resolvable is
   requested from the user before the message is sent (US5 AC1).
2. Send it fully resolved — confirm the conversation's existing provider/model selection and prior
   messages are unchanged/preserved, and the resolved prompt text becomes the new user message (US5
   AC2, SC-006).
3. Attempt to insert a prompt whose required capability isn't met by the conversation's currently
   selected model — confirm the user is warned before anything is sent (US5 AC3).
4. `GET /api/v1/prompts/{id}/executions` — confirm a `PromptExecution` with
   `Origin: ConversationInsertion` and a `resultMessageId` pointing at the new chat message now
   exists, and that it counted toward `PromptUsageStatistics`.

**Pass condition**: matches spec.md User Story 5's three acceptance scenarios; conversation context is
never lost or reset (SC-006, 100% of attempts).

## Scenario 6 — RAG and Memory context from a prompt (User Story 6)

1. Create a prompt with `useRagContext` targeting a Knowledge Base with indexed content; execute it
   via `POST /api/v1/prompts/{id}/executions`; confirm the persisted `PromptExecutionResult` includes
   `ragCitationsJson` and that the response is grounded in that content, with retrieved context
   clearly distinguishable from the prompt's own instructions (US6 AC1).
2. Repeat with `useMemoryContext` against a user with existing stored memories; confirm
   `memoryReferencesJson` is populated and relevant stored preferences are reflected (US6 AC2).
3. Execute with both flags set; inspect the assembled request (via logs/debug, not the AI output
   alone) and confirm system instructions, developer instructions, the prompt template, variable
   values, RAG context, and memory context remain structurally separated per research.md Decision 14,
   and that no combination of variable/RAG/memory content changed the system-level instructions'
   effect (US6 AC3, FR-092).

**Pass condition**: matches spec.md User Story 6's three acceptance scenarios.

## Scenario 7 — Export and import prompts (User Story 7 / SC-007)

1. `POST /api/v1/prompts/export` with a single `promptIds` entry — confirm the returned file contains
   metadata, content, variables, current version, model settings, and tags (US7 AC1).
2. `POST /api/v1/prompts/export` with **multiple** `promptIds` — confirm a single file is returned
   bundling each as an independent entry in the `prompts` array (US7 AC2, research.md Decision 13).
3. Delete the original prompt(s); `POST /api/v1/prompts/import` with the exported file — confirm every
   prompt is recreated, independently owned, with a fresh version-1 history, matching the originals in
   content/variables/settings with zero manual correction (US7 AC3/AC4, SC-007).
4. Corrupt one entry in a multi-prompt export file (e.g., remove a required field) and import it —
   confirm the **entire** import is rejected with a specific error and **nothing** is created, not
   even the valid entries (US7 AC3, FR-071, spec.md Edge Cases).

**Pass condition**: matches spec.md User Story 7's four acceptance scenarios.

## Scenario 8 — Concurrency and cross-cutting checks

1. Open the same prompt for editing in two sessions; save from session A, then attempt to save a
   different change from session B using its now-stale copy — confirm `409 Conflict`
   (`concurrency-conflict`) with no silent overwrite (FR-007).
2. Confirm no prompt content appears in any log sink accessible outside the owning user's own data
   (FR-091) — spot-check application logs after a full run of Scenarios 1–7.
3. Confirm every execution failure produced across the scenarios above surfaced a specific,
   user-visible error — grep the test run for any unhandled/500 response; there should be none
   attributable to this feature (SC-010).

**Pass condition**: zero silent failures, zero cross-user data exposure, and the one concurrent-edit
attempt in this scenario is rejected, not silently overwritten.
