---

description: "Task list template for feature implementation"
---

# Tasks: Chat Loading & Reply Feedback Fixes

**Input**: Design documents from `/specs/003-chat-loading-ux-fixes/`

**Prerequisites**: [plan.md](./plan.md) (required), [spec.md](./spec.md) (required for user stories), [research.md](./research.md), [data-model.md](./data-model.md)

**Tests**: Included. The constitution (§10 Testing Standards, §18 AI Coding Agent Rules — "Always update or add tests when changing observable behavior") makes test coverage for changed observable behavior non-optional for this repository, so every user story below includes test tasks.

**Organization**: Tasks are grouped by user story (from [spec.md](./spec.md)) to enable independent implementation and testing of each story. All work is frontend-only, under `src/AskLucy.Web/ClientApp/src/features/chat/` — no backend, contract, or data-model changes (see plan.md).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- Every task includes its exact file path

## Path Conventions

All paths are relative to the repository root, under the existing frontend project:
`src/AskLucy.Web/ClientApp/src/features/chat/`

---

## Phase 1: Setup

**Purpose**: Establish a known-good baseline before touching shared files

- [x] T001 Run `npm run lint` and `npm run test` in `src/AskLucy.Web/ClientApp` and confirm both pass cleanly, so later diffs to `ChatPage.tsx`, `MessageBubble.tsx`, `MessageBubble.test.tsx`, and `useChatStream.ts` can be judged against a known-good starting point.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared scaffold consumed by both P1 stories (US1 and US2 both modify the same conditional block in `ConversationView`); US3 and US4 do not depend on this phase and could start in parallel once T001 is done.

**⚠️ CRITICAL**: US1 and US2 both edit the same render branch in `ChatPage.tsx` — this phase adds the branch skeleton once so the two stories fill in non-overlapping placeholders instead of racing each other on the same lines.

- [x] T002 In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, update `ConversationView` to destructure `isPending`, `isError`, `error`, and `refetch` (alongside the existing `data`, `fetchNextPage`, `hasNextPage`, `isFetchingNextPage`) from `useChatMessages(chatId)`, and replace the current `messages.length === 0` empty-state check with a four-way scaffold: `chatId === null` → empty-state copy (unchanged), `isPending` → `{/* TODO US2: loading spinner */}`, `isError` → `{/* TODO US1: error + retry */}`, else → existing message-list rendering (unchanged).

**Checkpoint**: Foundational scaffold in place — US1 and US2 can now fill in their branches without touching each other's lines; US3 and US4 can proceed independently in parallel with this phase or after it.

---

## Phase 3: User Story 1 - Always land on the conversation I clicked (Priority: P1) 🎯 MVP

**Goal**: The "Start a conversation with Ask Lucy." empty state is shown only when no conversation is selected — never as a fallback while a selected conversation is loading or has failed to load; a failed load shows a visible, user-facing error with a manual Retry action.

**Independent Test**: Throttle/block the conversation-messages network request, click a conversation with existing messages, and confirm the chat area shows a loading state then either the messages or a Retry-able error — never the "Start a conversation with Ask Lucy." placeholder while that conversation is selected.

### Tests for User Story 1

- [x] T003 [P] [US1] Add test "shows the empty-state placeholder when no conversation is selected" (chatId null) to NEW file `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx`, following the MSW + QueryClientProvider pattern in `ChatSidebar.a11y.test.tsx`.
- [x] T004 [P] [US1] Add test "never shows the empty-state placeholder while the selected conversation's messages are pending" to `ChatPage.test.tsx` (mock a slow/never-resolving `GET /api/v1/chats/:id/messages` via MSW and assert the placeholder text is absent).
- [x] T005 [P] [US1] Add test "shows a visible error state with a Retry button when the selected conversation's messages fail to load" to `ChatPage.test.tsx` (mock a 500 response via MSW), asserting the empty-state placeholder is NOT shown instead.
- [x] T006 [P] [US1] Add test "clicking Retry re-fetches and shows the conversation's messages after a prior load failure" to `ChatPage.test.tsx` (MSW: first response errors, second succeeds).

### Implementation for User Story 1

- [x] T007 [US1] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, implement the `isError` branch from T002's scaffold: render a visible error message with `role="alert"` and an MUI `Button` labeled "Retry" wired to the destructured `refetch()` (FR-004).
- [x] T008 [US1] In the same file, confirm the empty-state branch only renders on `chatId === null` (per T002's scaffold) — remove any remaining reliance on `messages.length === 0` for this purpose (FR-001).

**Checkpoint**: User Story 1 is fully functional and independently testable — the empty-state bug is fixed even before US2's spinner exists (the loading branch can temporarily render minimal text; US2 replaces it with the real spinner next). T027 (rapid-switch regression test) and T028 (error-state a11y check) — listed at the end of this file to avoid renumbering — are also part of this story's completion.

---

## Phase 4: User Story 2 - See a loading indicator while a conversation opens (Priority: P1)

**Goal**: Selecting a conversation shows a visible loading spinner within 100ms, replaced by that conversation's messages or (per US1) an error state once the fetch settles.

**Independent Test**: Throttle the conversation-messages network request, click a conversation, and confirm a spinner appears near-instantly and is replaced once the fetch resolves.

### Tests for User Story 2

- [x] T009 [P] [US2] Add test "shows a loading spinner when a conversation is selected and its messages are pending" to `ChatPage.test.tsx` (assert a `role="status"` element is present while MSW holds the response open).
- [x] T010 [P] [US2] Add test "the loading spinner is replaced by the conversation's messages once the fetch resolves" to `ChatPage.test.tsx`.
- [x] T011 [P] [US2] Add accessibility test in NEW file `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.a11y.test.tsx`, following the `jest-axe` pattern in `ChatSidebar.a11y.test.tsx`, covering the loading-spinner state.

### Implementation for User Story 2

- [x] T012 [US2] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, implement the `isPending` branch from T002's scaffold: render MUI `CircularProgress` with `role="status"`, `aria-live="polite"`, and an accessible label (e.g. "Loading conversation…") (FR-002, FR-003).
- [ ] T013 [US2] Manually verify via `quickstart.md` Scenario 1 that the spinner appears within 100ms of selection (SC-002) with no artificial delay/debounce between selection and render.

**Checkpoint**: User Stories 1 AND 2 together fully resolve the conversation-switching bug and are independently verified.

---

## Phase 5: User Story 3 - Know the assistant is working on a reply (Priority: P2)

**Goal**: Sending a message shows an animated three-dot "thinking" indicator within 100ms, replaced by streamed content as it arrives; a failed send surfaces a visible, retryable error instead of a silent/stuck indicator.

**Independent Test**: Send a message and observe the reply-bubble area — the three-dot indicator appears immediately, is replaced by streamed text as it arrives, and (when the request is blocked) is replaced by a Retry-able error.

### Tests for User Story 3

- [x] T014 [P] [US3] Add test "renders three animated dots with role=status" in NEW file `src/AskLucy.Web/ClientApp/src/features/chat/components/ThinkingIndicator.test.tsx`.
- [x] T015 [P] [US3] Add test "shows ThinkingIndicator instead of MessageBubble for the in-flight assistant reply while no content has streamed in yet" to `ChatPage.test.tsx` (mock `POST /api/v1/ai/chat` SSE response with a delayed first chunk).
- [x] T016 [P] [US3] Add test "ThinkingIndicator is replaced by streamed content as soon as the first chunk arrives" to `ChatPage.test.tsx`.
- [x] T017 [P] [US3] Add test "a failed send surfaces a Snackbar error with a Retry action that resends the same message content" to `ChatPage.test.tsx` (mock `POST /api/v1/ai/chat` to fail, click Retry, mock succeeds, assert the original message content is resent).

### Implementation for User Story 3

- [x] T018 [P] [US3] Create `ThinkingIndicator` component in NEW file `src/AskLucy.Web/ClientApp/src/features/chat/components/ThinkingIndicator.tsx`: three-dot animation via MUI `keyframes`/`sx`, `role="status"`, `aria-live="polite"`, `aria-label="Ask Lucy is thinking"`, no reduced-motion fallback variant (FR-006, FR-011).
- [x] T019 [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`'s message-list rendering, render `ThinkingIndicator` instead of `MessageBubble` for any message where `isStreaming && message.content === ''` (FR-006, FR-007).
- [x] T020 [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts`, store the most recently attempted user message content in a ref at the top of `send`, and add a `retry` callback to the hook's return value that re-invokes `send` with that stored content, clearing `error` first (FR-008).
- [x] T021 [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, add a "Retry" action button to the existing error `Snackbar`/`Alert` (currently rendered around line 214-218), wired to the new `retry` from T020 (FR-008).

**Checkpoint**: User Story 3 is independently functional — thinking indicator and retryable send failures both verified, with no dependency on US1/US2's conversation-loading fix. T029 (ThinkingIndicator a11y check) — listed at the end of this file to avoid renumbering — is also part of this story's completion.

---

## Phase 6: User Story 4 - Reply bubbles show only the answer (Priority: P3)

**Goal**: No assistant reply bubble (new or historical) displays provider/model attribution text; the underlying data is still retained, only its display is removed.

**Independent Test**: View any assistant reply (new or from history) and confirm no "Provider · Model" caption is rendered anywhere in the bubble.

### Tests for User Story 4

- [x] T022 [P] [US4] Update the existing test `renders provider/model metadata for assistant messages` in `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.test.tsx` (currently asserts `'OpenAI · gpt-4'` renders) to instead assert the caption is absent even when `provider`/`model` are set on the message — rename it to reflect the new expectation (e.g. "does not render provider/model metadata even when present").
- [x] T023 [P] [US4] Verify the existing test `does not render metadata caption when absent` in the same file still passes unchanged after T022/T024 (no action needed if consistent — confirm only).

### Implementation for User Story 4

- [x] T024 [US4] In `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx`, remove the provider/model attribution `<Typography variant="caption">` block (currently lines 41-45), leaving `ChatMessage.provider`/`ChatMessage.model` and all other rendering (attachments, citations, markdown content) unchanged (FR-009, FR-010).

**Checkpoint**: All four user stories are now independently functional and verified.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final verification across all four stories together

- [x] T025 [P] Run `npm run lint` and `npm run test` in `src/AskLucy.Web/ClientApp` and resolve any regressions across all changed/new files.
- [ ] T026 Manually run through `quickstart.md` Scenarios 1-4 against the running dev server (`npm run dev` + backend) to validate the end-to-end experience, including the reduced-motion emulation check in Scenario 4.

---

## Additional Tasks (from `/speckit-analyze` remediation)

**Purpose**: Close two gaps found by cross-artifact analysis. Numbered here (after Phase 7) to avoid renumbering T001-T026, but each belongs logically to the story/phase noted — treat them as required for that story's checkpoint, not as afterthought polish.

- [x] T027 [P] [US1] Add regression test "shows the last-selected conversation's messages when the user clicks conversation B before conversation A's messages fetch resolves" to `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx` (MSW: delay A's response longer than B's; select A then immediately select B; assert only B's content ever renders and A's late-resolving response never overwrites it) (FR-005, US1 Acceptance Scenario 3). Logically part of Phase 3 (User Story 1).
- [x] T028 [P] [US1] Extend `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.a11y.test.tsx` (created in T011) with a jest-axe check for the error/Retry state: mock a failed messages fetch, render, run `axe()`, assert no violations (constitution §7 Accessibility, §10 Testing Standards). Depends on T011 (file must exist) and T007 (error branch implemented). Logically part of Phase 3 (User Story 1).
- [x] T029 [P] [US3] Extend `src/AskLucy.Web/ClientApp/src/features/chat/components/ThinkingIndicator.test.tsx` (created in T018) with a jest-axe check: run `axe()` on the rendered indicator, assert no violations (constitution §7, §10). Depends on T018. Logically part of Phase 5 (User Story 3).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup (T001) — BLOCKS US1 (Phase 3) and US2 (Phase 4) only, since both edit the same `ConversationView` branch scaffold. US3 (Phase 5) and US4 (Phase 6) do not depend on Phase 2 and may start immediately after Phase 1.
- **Polish (Phase 7)**: Depends on all four user story phases being complete.

### User Story Dependencies

- **US1 (P1)**: Depends on Foundational (T002). No dependency on US2/US3/US4.
- **US2 (P1)**: Depends on Foundational (T002). Fills in a different branch of the same scaffold as US1 (non-conflicting once T002 lands) — can be implemented in parallel with US1 by a different contributor, or immediately after.
- **US3 (P2)**: No dependency on Foundational, US1, or US2 — different concern (message send vs. conversation switch), different code paths (`useChatStream.ts` + message-list item rendering).
- **US4 (P3)**: No dependency on any other story — isolated to `MessageBubble.tsx`.
- **T027 (US1)**: Depends on T003-T006 existing (same file) — can run any time after Foundational; no dependency on T007/T008 since it's an independent `it` block.
- **T028 (US1)**: Depends on T011 (file must exist) and T007 (error branch implemented) — runs after both, so after Phase 4's T011 lands even though the assertion belongs to US1.
- **T029 (US3)**: Depends on T018 (component must exist) — can run immediately after T018, in parallel with T019-T021.

### Within Each User Story

- Tests are written first and should fail before the corresponding implementation task lands.
- Within US1/US2: scaffold (T002) → branch implementation → manual/automated verification.
- Within US3: `ThinkingIndicator` component (T018) before it's wired into `ChatPage.tsx` (T019); `useChatStream` retry plumbing (T020) before the Snackbar action button that calls it (T021).
- Within US4: test update (T022) alongside the removal (T024) — both describe the same behavior change.

### Parallel Opportunities

- T001 blocks everything but is a single quick task.
- After T001: US3's tests/component (T014-T018) and US4's tests/removal (T022-T024) can start immediately in parallel with Phase 2/US1/US2, since they touch entirely different files (`ThinkingIndicator.tsx`, `useChatStream.ts`, `MessageBubble.tsx`) with no shared state.
- Within US1: T003-T006 (all in the same new file, but independent `it` blocks) can be drafted in parallel, then implementation tasks T007-T008 land sequentially in the same file.
- Within US2: T009-T011 in parallel, then T012-T013 sequentially.
- Within US3: T014-T017 in parallel, then T018 (new file) can run in parallel with T020 (different file: `useChatStream.ts`), followed by T019 and T021 (both depend on T018/T020 respectively).
- Within US4: T022-T023 in parallel with each other; T024 can happen alongside them since it's the change the tests are written against.

---

## Parallel Example: User Story 3

```bash
# Launch all tests for User Story 3 together:
Task: "Add test 'renders three animated dots with role=status' in ThinkingIndicator.test.tsx"
Task: "Add test 'shows ThinkingIndicator instead of MessageBubble while no content has streamed' in ChatPage.test.tsx"
Task: "Add test 'ThinkingIndicator replaced by streamed content on first chunk' in ChatPage.test.tsx"
Task: "Add test 'failed send surfaces Retry-able Snackbar error' in ChatPage.test.tsx"

# Then implementation, two independent files in parallel:
Task: "Create ThinkingIndicator component in ThinkingIndicator.tsx"
Task: "Add retry callback + last-attempted-content ref in useChatStream.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 + 2 Only)

1. Complete Phase 1: Setup (T001).
2. Complete Phase 2: Foundational (T002) — required by both P1 stories.
3. Complete Phase 3: User Story 1 (T003-T008).
4. Complete Phase 4: User Story 2 (T009-T013).
5. **STOP and VALIDATE**: Run `quickstart.md` Scenario 1 end-to-end. This alone fixes the most severe reported bug (empty-state flash on conversation switch).
6. Deploy/demo if ready — US3 and US4 can ship in a follow-up increment.

### Incremental Delivery

1. Setup + Foundational → scaffold ready.
2. US1 + US2 (both P1, same scaffold) → conversation-switching bug fully fixed → validate via quickstart Scenario 1 → ship (MVP).
3. US3 (P2) → thinking indicator + retryable send failures → validate via quickstart Scenario 2 → ship.
4. US4 (P3) → attribution line removed → validate via quickstart Scenario 3 → ship.
5. Each increment adds value without breaking a previously shipped one — no story's implementation touches another story's files except the shared T002 scaffold.

### Parallel Team Strategy

With multiple contributors, after T001:

- Contributor A: Foundational (T002) → US1 → US2.
- Contributor B: US3 (T014-T021) — entirely independent files.
- Contributor C: US4 (T022-T024) — entirely independent file.

All three converge at Phase 7 (Polish) once their respective phases are complete.

---

## Notes

- [P] tasks = different files (or independent `it` blocks awaiting later sequential implementation), no blocking dependencies.
- [Story] label maps every phase-3+ task to its user story for traceability back to spec.md.
- No contracts/ directory exists for this feature (no new backend endpoints) — see plan.md.
- Commit after each task or logical group, per repository convention (Conventional Commits — constitution §11).
- Stop at any checkpoint to validate a story independently before continuing.
