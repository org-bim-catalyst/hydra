# Tasks: Reply Action Bar

**Input**: Design documents from `specs/046-reply-action-bar/`
**Prerequisites**: plan.md, research.md, data-model.md, contracts/reply-action-bar.md, quickstart.md

**Tests**: Existing component/a11y tests already cover this control (specs/039); this feature
updates them in place rather than adding a parallel suite, and adds new assertions for Copy.

**Organization**: Tasks are grouped by user story from spec.md to enable independent
implementation and testing. All work is confined to
`src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx` and its two test
files; `ChatPage.tsx` is unmodified (FR-008).

## Phase 1: Setup

- [x] T001 Confirm `RiFileCopyLine` and `RiCheckLine` are exported by the installed
  `@remixicon/react` version used in `src/AskLucy.Web/ClientApp/package.json` (already verified
  during planning; re-check only if the lockfile has since changed).

## Phase 2: Foundational (blocking prerequisites)

- [x] T002 In `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.tsx`,
  extend the icon import to add `RiFileCopyLine`, `RiCheckLine` from `@remixicon/react`
  alongside the existing `RiAttachment2, RiPlayFill, RiStopFill`.
- [x] T003 In the same file, remove the `Paper` `sx`'s conditional `...(showReplayControl && { pb: 4 })`
  entry (contracts/reply-action-bar.md — the bubble no longer reserves space for an in-bubble
  control) and remove the old absolutely-positioned replay `<Tooltip>`/`<span>`/`<IconButton>`
  block (current lines ~197-213) — its markup is replaced, not duplicated, by the new action row
  built in Phase 3/4.

**Checkpoint**: Component still renders replies with attachments/citations/attribution
unaffected; no action row yet (rebuilt next).

## Phase 3: User Story 3 - Replay/stop behavior is unchanged (Priority: P1)

**Goal**: Relocate the existing Replay/Stop control into the new below-bubble row with zero
behavioral change.

**Independent Test**: Run the specs/039 US5 acceptance scenarios (play, stop, restart,
single-active-playback, disabled-while-speaking-or-muted) against the relocated control.

- [x] T004 [US3] In `MessageBubble.tsx`, add the Reply Action Row container (a `Stack` in
  normal document flow, `direction="row"`, `spacing={0.5}`, `justifyContent: 'flex-start'`,
  rendered as a sibling immediately after the closing `</Paper>`, inside the same outer `Box`
  that already sets `justifyContent: isUser ? 'flex-end' : 'flex-start'`), gated on
  `!isUser && Boolean(message.id)` per contracts/reply-action-bar.md.
- [x] T005 [US3] Inside that row, re-add the Replay/Stop `IconButton` gated on
  `showReplayControl`, preserving its exact `disabled`, `onClick`, and `aria-label` logic from
  the removed block (T003) unchanged — only change `fontSize="small"` to `size={16}` on
  `RiPlayFill`/`RiStopFill` per FR-006/research.md Decision 4.
- [x] T006 [US3] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.test.tsx`:
  adjust any assertion that locates the Replay/Stop button via its old absolute-positioned
  container to instead find it in the new action row; confirm all existing play/stop/disabled
  assertions still pass unmodified in substance.
- [x] T007 [US3] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/MessageBubble.a11y.test.tsx`
  for the relocated control's new DOM position (same `aria-label`s, so accessible-name
  assertions should be unaffected; update only structural/landmark assertions if any).

**Checkpoint**: Replay/Stop fully functional again, now positioned below the bubble, smaller
icon, with all specs/039 behavior intact — independently shippable.

## Phase 4: User Story 1 - Copy a reply's text (Priority: P1)

**Goal**: Add a working, never-silent Copy action to the action row.

**Independent Test**: View any completed assistant reply, click Copy, confirm exact text on
clipboard and a visible success confirmation; simulate a rejected `writeText` and confirm a
visible failure indication.

- [x] T008 [US1] In `MessageBubble.tsx`, add local state
  `const [copyStatus, setCopyStatus] = useState<'idle' | 'success' | 'error'>('idle')` and the
  `handleCopy` callback exactly as specified in contracts/reply-action-bar.md (await
  `navigator.clipboard.writeText(message.content)`, `try/catch` setting `success`/`error`,
  `finally` scheduling a reset to `idle` via `window.setTimeout(..., 2000)`).
- [x] T009 [US1] In the action row from T004, add the Copy `IconButton` (`size="small"`,
  `onClick={handleCopy}`, `aria-label="Copy"`) rendering `RiCheckLine size={16}` when
  `copyStatus === 'success'` else `RiFileCopyLine size={16}`, wrapped in a `Tooltip` whose
  title reflects `copyStatus` ("Copied" / "Copy failed" / "Copy") per contracts/reply-action-bar.md.
  This `IconButton` renders whenever the row itself renders (i.e., even when `showReplayControl`
  is `false`), satisfying FR-009.
- [x] T010 [US1] Add test cases to `MessageBubble.test.tsx`: clicking Copy calls
  `navigator.clipboard.writeText` with `message.content`; on a resolved promise the button's
  accessible state/tooltip reflects success; on a rejected promise (mock `writeText` to reject)
  it reflects a visible failure, never a silent no-op or thrown/unhandled rejection in the test.
  Also assert no Copy action renders for a user message, and none renders for a message with no
  `id` (streaming placeholder).
- [x] T011 [US1] [P] Add an assertion to `MessageBubble.a11y.test.tsx` that the Copy button has
  a non-empty accessible name in both its idle and success/error states (axe check covers the
  rest of the row already exercised by T007).

**Checkpoint**: Copy fully functional and independently testable/shippable alongside US3.

## Phase 5: User Story 2 - Reply actions read as a single, familiar row (Priority: P2)

**Goal**: Confirm the combined row's visual presentation matches spec (left-aligned, below
bubble, smaller icons) as an integration-level check across both controls together.

**Independent Test**: View an assistant reply with both Replay and Copy available; confirm they
render together in one left-aligned row beneath the bubble with visibly smaller icons than the
pre-feature control.

- [x] T012 [US2] Add/extend a `MessageBubble.test.tsx` case asserting the action row container
  renders both the Replay/Stop button and the Copy button as siblings within one row when
  `onReplay` is provided, and only the Copy button when it is not (FR-009), confirming T004's
  gating and T005/T009's placement are wired correctly together.
- [ ] T013 [US2] Manual/visual check per quickstart.md steps 1-2: run the dev server, produce a
  reply, and visually confirm left alignment, no overlap with bubble text, and smaller icon size
  compared to the feature's `git diff` baseline. NOT performed by the agent — this sandbox has no
  local SQL Server/secrets to run the full backend and no browser-automation tool available
  (Windows host, not the Linux container the `run` skill's screenshot path assumes). Structural
  verification instead comes from T006/T007/T010-T012's DOM-level assertions (exact Stack/Box
  nesting, role, and `size={16}` icon props all render as specified). Recommend a quick manual
  look in the running app before/shortly after this ships.

**Checkpoint**: All three user stories independently verified; combined feature complete.

## Phase 6: Polish & Cross-Cutting

- [x] T014 Update `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx` only if
  any of its assertions reach into `MessageBubble`'s old in-bubble replay DOM structure
  (per [[page_level_tests_miss_component_changes]] — check even though `ChatPage.tsx` itself is
  unmodified). Found and fixed: 7 sites scoped `within(reply)` to `.closest('.MuiPaper-root')`,
  which stopped finding the relocated Replay/Stop button once it moved outside the `Paper`;
  rescoped each to `.closest('.MuiPaper-root')?.parentElement` (the new bubble+action-row wrapper).
- [x] T015 Run the full frontend suite and type check from
  `src/AskLucy.Web/ClientApp`: `npm test` and `npx tsc -b --noEmit` (see
  [[frontend_tsc_noemit_silent_noop]] — the bare `tsc --noEmit` is a silent no-op in this repo).
  877/877 tests pass (confirmed with `--no-file-parallelism` after the default parallel run showed
  unrelated cross-test timeout flakiness under load); `tsc -b --noEmit` clean; `eslint` clean.
- [x] T016 Update the outdated comment block at the top of `MessageBubble.tsx` (currently
  describing the replay control as living "in the lower-right corner") and the `showReplayControl`
  comment referencing "research.md Decision 7" to reflect the new below-bubble row placement,
  keeping the specs/039 FR references intact since that behavior is unchanged.

## Dependencies & Execution Order

- Phase 1 → Phase 2 → {Phase 3, Phase 4} → Phase 5 → Phase 6.
- Phase 3 (US3, replay relocation) and Phase 4 (US1, Copy) both build on Phase 2's removal of
  the old block and the new row container started in T004; T004 itself is listed under Phase 3
  because Replay is P1 and structurally first, but Phase 4's Copy button (T009) is additive to
  the same row and has no other dependency on Phase 3 finishing — a single developer will in
  practice do T004 once and add both buttons before running either story's tests.
- Phase 5 (US2) depends on both Phase 3 and Phase 4 being complete (it verifies them together).
- T011 is marked [P] — it touches only the a11y test file, independent of T010's file.

## Implementation Strategy

**MVP scope**: Phases 1-4 (Setup, Foundational, US3 relocation, US1 Copy) constitute a complete,
shippable improvement — a working, non-regressing, smaller-icon action row with Copy. Phase 5
is a verification-only pass with no new behavior; Phase 6 is cleanup/docs.

Given the single-file scope, implement sequentially (T001-T016) in one pass rather than
splitting across parallel workstreams — the "[P]" marker on T011 is the only genuine
parallelization opportunity.
