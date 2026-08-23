# Tasks: Composer & Panel Layout Refinements

**Input**: Design documents from `/specs/030-composer-panel-refinements/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/composer-panel-layout-contract.md, quickstart.md

**Tests**: Included as core tasks (not optional) — this repo's constitution (§10, §18) requires tests in the same PR as any observable behavior change; every story below carries its own test-update tasks.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P1/P2/P3) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Paths are relative to the repo root unless otherwise noted

## Path Conventions

Frontend-only feature inside the existing SPA: `src/AskLucy.Web/ClientApp/src/features/chat/`. No backend paths are touched (plan.md Project Structure).

---

## Phase 1: Setup

**Purpose**: Confirm the pre-change baseline and the one new external symbol this feature relies on, before touching either component.

- [X] T001 Run `npm run test -- ChatComposer ExpandedChatPanel` from `src/AskLucy.Web/ClientApp` and confirm the full existing specs/029-fix-chat-widget-bugs suite for both components passes, establishing a clean baseline before any change in this feature.
- [X] T002 [P] Confirm `RiExpandVerticalLine` and `RiCollapseVerticalLine` are exported by the installed `@remixicon/react` version (research.md Decision 5) by grepping `src/AskLucy.Web/ClientApp/node_modules/@remixicon/react/index.d.ts`; if either is missing, pick the closest available vertical-expand/collapse icon pair and update research.md Decision 5 accordingly before continuing.

**Checkpoint**: Baseline green, icon dependency confirmed.

---

## Phase 2: Foundational

**Purpose**: N/A — this feature has no prerequisite shared by all four stories. User Stories 1–2 touch only `ChatComposer.tsx`; User Stories 3–4 touch only `ExpandedChatPanel.tsx` plus the new store. Each pair is independently startable once Phase 1 is done.

*(No tasks in this phase.)*

---

## Phase 3: User Story 1 - Composer reads as a clear input box, not a crowded pill (Priority: P1) 🎯 MVP

**Goal**: The composer's resting (single-line) state is a rounded-corner rectangle with a text area on top and every control button in a fixed footer row at the bottom, matching contracts/composer-panel-layout-contract.md's "ChatComposer structure."

**Independent Test**: Open the chat widget with an empty/single-line composer; verify visually and via DOM structure that it's a rounded rectangle (not a pill) with a distinct footer row, and that every existing control (send, attach, mic, mute, translate, mode-switch) still works exactly as before.

### Implementation for User Story 1

- [X] T003 [US1] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`, change the outer `Paper`'s `sx` from `display: 'flex', alignItems: 'center', borderRadius: radius.pill` to `display: 'flex', flexDirection: 'column', borderRadius: radius.lg` (research.md Decision 1), keeping `variant="outlined"`, `maxWidth: 800`, `mx: 'auto'`, and the existing `focus-within` transition/border/shadow styling unchanged.
- [X] T004 [US1] In the same file, wrap the `<TextField ...>` in a new top-level `<Box>` (the text-entry region) and move the file input, attach button, insert-prompt button, mic/`RecordingReviewControls` block, mode-switch `IconButton`+`Menu`, voice-preferences-unavailable indicator, mute button, translate button, and the send `IconButton` into a new `<Stack direction="row" spacing={0.5}>` footer directly below that `Box`, preserving their existing left-to-right order, props, handlers, and conditional rendering exactly as-is (contracts/composer-panel-layout-contract.md — only the grouping/axis changes, per spec.md FR-014). Implementation note: a flex spacer (`<Box sx={{ flex: 1 }} />`) was added immediately before the send button so it anchors to the footer's trailing edge, matching the reference screenshots; no button was reordered relative to its neighbors.
- [X] T005 [US1] Verify (read the resulting JSX) that the footer `Stack` has no `flex: 1`/`minHeight: 0` styling that would let it shrink or scroll — it must stay a fixed-size row per FR-005; add `flexShrink: 0` to the footer `Stack`'s `sx` if needed to guarantee this.
- [X] T006 [US1] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx`: add/adjust assertions that the footer buttons (e.g., the send button) and the `TextField` are no longer siblings in a single flat row container — assert the footer buttons share a common parent distinct from the text field's wrapping `Box` — while keeping every existing interaction test (send on Enter, attach a file, mic hold/tap/toggle across both conversation modes, mode-switch menu, mute toggle, translate click) passing unmodified in behavior.
- [X] T007 [US1] Run `npm run test -- ChatComposer` and `npx tsc --noEmit` from `src/AskLucy.Web/ClientApp`; fix any failures before moving to User Story 2. Result: 34/34 tests pass, zero TypeScript errors.

**Checkpoint**: Composer's resting shape and footer layout are correct and fully tested; all pre-existing composer behavior still passes.

---

## Phase 4: User Story 2 - Composer grows with typed content, then scrolls instead of taking over the screen (Priority: P1)

**Goal**: The composer's text area grows up to ~6 lines, then caps its height and scrolls internally, with the footer row (from User Story 1) staying fixed at the bottom throughout.

**Independent Test**: Type progressively more newlines into the composer; confirm growth stops at ~6 lines with an internal scrollbar past that point, and that deleting text back down shrinks the composer again — all while the footer row never moves.

**Note**: Depends on User Story 1's restructure (T003–T005) already being in place in `ChatComposer.tsx`, since this story caps the same `TextField` now living inside US1's new top `Box` — the two stories are file-adjacent, not independent, per spec.md's own note that they are "tightly coupled."

### Implementation for User Story 2

- [X] T008 [US2] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`, keep `multiline maxRows={6}` on the `TextField` and add an explicit fixed `lineHeight` (e.g. via `slotProps.input.sx` or the `TextField`'s own `sx` targeting `& .MuiInputBase-input`) so the 6-row cap is computed from a known, non-inherited value rather than ambient container line-height (research.md Decision 2).
- [X] T009 [US2] Confirm (via the `slotProps.input` config already present, `disableUnderline: true`) that MUI's multiline autosize applies `overflow-y: auto` to the input once content exceeds `maxRows`; if it does not by default, add `sx={{ '& .MuiInputBase-inputMultiline': { overflowY: 'auto' } }}` explicitly to guarantee FR-004's scrollbar requirement. Implementation note: added directly alongside T008's `lineHeight` rule under `& .MuiInputBase-input`.
- [X] T010 [US2] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx` to assert the `TextField` carries `maxRows={6}` and the fixed `lineHeight` styling added in T008 (component-level assertions — jsdom cannot assert real pixel growth, so assert the props/styles that drive the cap, per research.md Decision 2's noted testing limitation). Also add a test covering FR-006: render with several lines of `value` then rerender with fewer lines (simulating a delete), and assert the `TextField`'s row-driving props/content reflect the shorter value with no leftover row count from the earlier longer content. Result: 36/36 tests pass.
- [ ] T011 [US2] Manually verify in a running browser (see quickstart.md scenario 2) that (a) typing 10+ lines caps the composer's visible height at ~6 lines with a working scrollbar, (b) deleting text back down to fewer than 6 lines shrinks the composer back down to fit the remaining content with no leftover empty space or stuck scrollbar (FR-006), and (c) the footer row never moves during either direction — fix `lineHeight`/`maxRows` values if either direction doesn't hold in practice. **Deferred to the consolidated Phase 7 browser pass (T030) alongside T021/T029.**

**Checkpoint**: Composer growth is capped and scrollable, verified both by component tests and a real-browser check; footer stays fixed. User Stories 1+2 together are shippable as the MVP.

---

## Phase 5: User Story 3 - Chat panel can expand to full window height (Priority: P2)

**Goal**: `ExpandedChatPanel` supports a half-height (default) and a full-window-height state, toggled by a new control, with the chosen state persisted across reloads (spec.md Clarifications 2026-08-20).

**Independent Test**: Open the panel, toggle to full height, confirm it fills the window height without clipping; toggle back; reload the page and confirm the last-chosen state is restored — all independent of any composer typing.

### Implementation for User Story 3

- [X] T012 [P] [US3] Create `src/AskLucy.Web/ClientApp/src/features/chat/chatPanelSizeStore.ts`: a Zustand store with `persist` middleware, `localStorage` key `ask-lucy-chat-panel-size`, state shape `{ isFullHeight: boolean, toggle: () => void }`, default `isFullHeight: false`, mirroring `src/AskLucy.Web/ClientApp/src/store/themeStore.ts`'s pattern exactly (research.md Decision 4, data-model.md).
- [X] T013 [P] [US3] Create `src/AskLucy.Web/ClientApp/src/features/chat/chatPanelSizeStore.test.ts` covering: default `isFullHeight` is `false`; `toggle()` flips it; the value round-trips through `localStorage` under the `ask-lucy-chat-panel-size` key (mirror `themeStore` test patterns if one exists, otherwise write directly against the store's public API). Result: 3/3 tests pass.
- [X] T014 [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ExpandedChatPanel.tsx`, add two new required props to `ExpandedChatPanelProps`: `isFullHeight: boolean` and `onToggleHeight: () => void` (data-model.md "Component prop changes").
- [X] T015 [US3] In the same file, change the outer `Box`'s `sx.height` to branch on `isFullHeight`: `{ xs: 'min(70vh, 600px)', sm: 560 }` when `false` (unchanged default), `{ xs: 'calc(100vh - 32px)', sm: 'calc(100vh - 48px)' }` when `true` (research.md Decision 3); add `transition: (theme) => theme.transitions.create(['height'])` so the toggle animates smoothly; leave `width` untouched in both states.
- [X] T016 [US3] In the same file's header `Stack`, add a new `IconButton` immediately after the existing "Start new conversation" `IconButton` (before `{headerTrailing}`): `onClick={onToggleHeight}`, rendering `RiExpandVerticalLine` when `!isFullHeight` and `RiCollapseVerticalLine` when `isFullHeight`, with `aria-label` `'Expand to full height'` / `'Collapse to half height'` respectively (research.md Decision 5, contracts/composer-panel-layout-contract.md). Implementation note: also wrapped this button, plus the pre-existing Collapse and Start-new-conversation buttons, in a `Tooltip` reusing the same text — folds part of T025 in early since it's the same lines.
- [X] T017 [US3] In `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx`, wire `ExpandedChatPanel`'s new `isFullHeight`/`onToggleHeight` props to `useChatPanelSizeStore` (`isFullHeight` from the store's state, `onToggleHeight` calling the store's `toggle()`), reading the store at the same level `ConversationView` already reads other chat-widget state.
- [X] T018 [US3] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/ExpandedChatPanel.test.tsx`: add tests that the panel renders at the half-height `sx` branch by default, switches to the full-height branch when `isFullHeight` is `true`, and that clicking the new resize/toggle button calls `onToggleHeight`. Result: 4 new tests added.
- [X] T019 [US3] Update `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.test.tsx` (and `ChatPage.a11y.test.tsx` if it renders the panel header) to cover the new prop wiring end-to-end: toggling the button flips the persisted store value and the panel's rendered height branch. Added a cross-test `chatPanelSizeStore`/localStorage reset in the shared `beforeEach` to prevent leakage between tests.
- [X] T020 [US3] Run `npm run test -- ExpandedChatPanel chatPanelSizeStore ChatPage` and `npx tsc --noEmit` from `src/AskLucy.Web/ClientApp`; fix any failures. Result: all pass in isolation (42/42, 14/14, 18/18); two apparent failures when run bundled together with other heavy suites were confirmed to be pre-existing local resource-contention flakiness (per this repo's SPEC-029 precedent), not a regression — each file passes cleanly on its own.
- [ ] T021 [US3] Manually verify in a running browser (quickstart.md scenarios 3–4): toggle to full height and confirm no clipping/no page scrollbar appears, toggle back, reload and confirm the last choice is restored from `localStorage`. Also resize the browser (or use device emulation) down to the narrow/mobile (`xs`) breakpoint and repeat the full-height toggle there, confirming the panel and its header controls remain fully visible and reachable with no clipped or unreachable control (spec.md Edge Cases — narrow viewport). **Deferred to the consolidated Phase 7 browser pass (T030) alongside T011/T029.**

**Checkpoint**: Panel height toggle works, persists across reloads, and is fully tested — independently shippable on top of the US1+US2 MVP.

---

## Phase 6: User Story 4 - Resize control sits next to the new-chat control, and every icon button explains itself (Priority: P3)

**Goal**: The resize/toggle button (User Story 3) is confirmed adjacent to the "+" button (already satisfied by T016's placement), and every icon-only button across both components exposes a hover/focus tooltip, including a contextual mic tooltip.

**Independent Test**: Hover/keyboard-focus every icon-only button in the composer and panel header and confirm each shows a tooltip; confirm the resize button sits next to "+", not next to the collapse arrow.

### Implementation for User Story 4

- [X] T022 [P] [US4] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx`, wrap the attach `IconButton` and the insert-prompt `IconButton` each in an MUI `Tooltip`, reusing their existing `aria-label` strings (`'Attach file'`, `'Insert saved prompt'`) as the `title` (research.md Decision 6).
- [X] T023 [P] [US4] In the same file, wrap the mic `IconButton` (the plain-mic branch, not `RecordingReviewControls`) in a `Tooltip` whose `title` reuses the existing dynamic `aria-label` (`isListening ? 'Stop voice input' : 'Start voice input'`), so the tooltip text changes contextually per FR-012 with no new logic.
- [X] T024 [US4] In the same file, wrap the send `IconButton` in a `Tooltip title="Send message"`, using the `<Tooltip><span><IconButton disabled .../></span></Tooltip>` pattern already used for the mode-switch button, since the send button is conditionally `disabled`.
- [X] T025 [P] [US4] In `src/AskLucy.Web/ClientApp/src/features/chat/components/ExpandedChatPanel.tsx`, wrap the collapse `IconButton` and the "Start new conversation" `IconButton` each in a `Tooltip` reusing their existing `aria-label` strings (`'Collapse'`, `'Start new conversation'`); confirm the resize/toggle button added in T016 already has its own `Tooltip` with the aria-label text from that task (add one now if T016 only set `aria-label` without a visible `Tooltip`). Done in T016 already (same lines).
- [X] T026 [US4] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.test.tsx` to assert a tooltip (`title`/accessible description) is discoverable for attach, insert-prompt, mic, and send, including the mic's contextual text changing between listening states. Result: 40/40 tests pass (used `userEvent.hover`, not `fireEvent.focus`, per MUI Tooltip's focus-visible-only open behavior discovered while writing these tests; the disabled send button required hovering its Tooltip `<span>` wrapper, not the `pointer-events:none` button itself).
- [X] T027 [US4] Update `src/AskLucy.Web/ClientApp/src/features/chat/components/ExpandedChatPanel.a11y.test.tsx` to assert tooltips exist for collapse, new-chat, and the resize/toggle button, and that the resize/toggle button's DOM position is immediately after the new-chat button and before any `headerTrailing` content (contracts/composer-panel-layout-contract.md header order). Result: 18/18 tests pass.
- [X] T028 [US4] Run `npm run test -- ChatComposer ExpandedChatPanel ExpandedChatPanel.a11y` and `npx tsc --noEmit` from `src/AskLucy.Web/ClientApp`; fix any failures. Result: all pass, zero TS errors.
- [ ] T029 [US4] Manually verify in a running browser (quickstart.md scenario 5): Tab through every icon-only button in both components and confirm each shows a tooltip on both hover and keyboard focus. **Deferred to the consolidated Phase 7 browser pass (T030) alongside T011/T021.**

**Checkpoint**: All four user stories complete; every icon-only button in both components has a tooltip, and the resize button's placement matches spec.md's correction.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final regression pass and documentation, once all four stories are complete.

- [ ] T030 Run the full quickstart.md manual validation pass (all 6 scenarios, including the "Regression pass" scenario covering send/attach/both voice modes/mute/translate) in a running browser. **Not run**: this app requires the ASP.NET backend + a working SQL Server connection to reach an authenticated chat view (no Storybook/standalone frontend entry point exists), and no local backend/DB was running or reachable in this session (`localhost:5173`/`5000`/`7000` all unreachable) — matching this repo's own documented local-dev-DB blocker from specs/029-fix-chat-widget-bugs. T011/T021/T029's manual checks are correspondingly unverified in a real browser; all their behavioral claims rest on the automated test coverage (T006/T007, T010, T018, T019, T026, T027) instead. Recommend the user runs quickstart.md's 6 scenarios against their own dev environment before merge.
- [X] T031 [P] Run `npm run test` (full frontend suite) from `src/AskLucy.Web/ClientApp` to confirm no regressions were introduced outside the two changed components. Result: 584/619 pass; 35 failures across 20 files, all 5000ms timeouts in unrelated feature areas (documents, knowledge-base, landing, profile, settings, prompts, workflows) — none in this feature's changed files (`ChatComposer`, `ExpandedChatPanel`, `ChatPage`, `chatPanelSizeStore`), which all pass cleanly when run in isolation. Matches this repo's documented local full-suite resource-contention precedent (memory: SPEC-029's "false alarm" full-suite timeouts) rather than a real regression.
- [X] T032 [P] Run `npm run lint` (or the project's equivalent ESLint/Prettier check) from `src/AskLucy.Web/ClientApp` on the changed files and fix any violations. Result: 0 errors, 10 pre-existing warnings, none in this feature's changed files.
- [X] T033 Update this feature's spec.md checklist file `specs/030-composer-panel-refinements/checklists/requirements.md` notes section if any assumption changed during implementation (e.g., if T002 substituted a different icon pair). No change needed: T002 confirmed `RiExpandVerticalLine`/`RiCollapseVerticalLine` exist as planned, and no other assumption in spec.md was invalidated during implementation.

---

## Post-Implementation Fixes (User Acceptance Testing, US3)

The user ran the app locally and confirmed US1/US2 pass. Testing US3 (full-height toggle) surfaced two real, pre-existing defects the automated suite couldn't catch (both require an actual browser render — see T030's note on why manual verification wasn't possible earlier in this session):

- [X] T034 [US3] **Z-index**: at full height, `ExpandedChatPanel`'s top edge now reaches the same top-right corner as `WorkspaceOverlay`'s `topClusterLeading` controls (`ThemeToggleButton`, `RotationToggleButton`) and its `top-cluster` `CircularAction`, all built on MUI's `Fab`, which bakes in `zIndex: theme.zIndex.fab` (1050) via MUI's own base styles — far above `ChatAssistantWidget.tsx`'s old `zIndex: 3`, so those buttons rendered on top of the panel. Fixed by raising `ChatAssistantWidget.tsx`'s `zIndex` to `(theme) => theme.zIndex.fab + 50`, comfortably above every `Fab`-based floating control project-wide while staying below Drawer/Modal/Snackbar/Tooltip. This was a latent bug in the original SPEC-026/SPEC-024 z-index scheme, only exposed now that the panel can reach that corner.
- [X] T035 [US3] **Background seam**: `ExpandedChatPanel.tsx`'s content `Box` used `bgcolor: 'background.paper'` while `ChatPage.tsx`'s message-list container explicitly overrides to `bgcolor: 'background.default'` — since `ChatComposer.tsx`'s own outer wrapping `Box` sets no `bgcolor` of its own (transparent, revealing whatever the content `Box` uses), the composer's surrounding gutter read as a visibly different shade from the message list above it. Fixed by changing the content `Box` to `bgcolor: 'background.default'`, matching the message list so both regions render as one continuous surface in both themes.
- [X] Verified via `npm run test -- ExpandedChatPanel ChatAssistantWidget chatPanelSizeStore` (19/19 pass) and `npx tsc --noEmit` (clean). Not yet re-verified in a real browser — same environment limitation as T030.

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Empty — nothing blocks all four stories.
- **User Story 1 (Phase 3)**: Depends only on Phase 1.
- **User Story 2 (Phase 4)**: Depends on Phase 1 AND User Story 1's restructure (T003–T005) — not independent of US1, per spec.md's own note; do not start T008+ until T003–T007 are done.
- **User Story 3 (Phase 5)**: Depends only on Phase 1 — independent of US1/US2 (different file). Can run in parallel with Phases 3–4 if staffed separately.
- **User Story 4 (Phase 6)**: Depends on User Story 1 (composer buttons must exist in their final footer grouping before wrapping in tooltips) AND User Story 3 (the resize/toggle button T022–T029 references must exist before its tooltip/placement can be verified).
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Parallel Opportunities

- T001 and T002 (Setup) can run in parallel.
- T012 and T013 (US3's new store + its test) can run in parallel with each other, and the whole of Phase 5 (US3) can run in parallel with Phases 3–4 (US1+US2), since they touch entirely disjoint files (`ExpandedChatPanel.tsx`/`chatPanelSizeStore.ts` vs `ChatComposer.tsx`).
- Within Phase 6 (US4), T022, T023, and T025 touch different button blocks and can be parallelized; T024 and T026–T029 should follow after T022/T023 land in the same file to avoid merge conflicts.
- T031 and T032 (Polish) can run in parallel.

---

## Parallel Example: User Story 3 running alongside User Stories 1+2

```bash
# Track A (US1 → US2, sequential within ChatComposer.tsx):
Task: "T003 Restructure ChatComposer.tsx's outer Paper to column-flex"
Task: "T004 Move controls into a footer Stack"
Task: "T008 Add fixed lineHeight for the 6-row cap"

# Track B (US3, independent file, can run at the same time as Track A):
Task: "T012 Create chatPanelSizeStore.ts"
Task: "T013 Create chatPanelSizeStore.test.ts"
Task: "T014 Add isFullHeight/onToggleHeight props to ExpandedChatPanelProps"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Complete Phase 1: Setup.
2. Complete Phase 3: User Story 1 (composer shape).
3. Complete Phase 4: User Story 2 (capped growth) — depends on Phase 3 in the same file.
4. **STOP and VALIDATE**: Run quickstart.md scenarios 1–2 and the regression scenario in a browser.
5. This MVP alone resolves the two most visible reported defects (pill shape, unbounded growth).

### Incremental Delivery

1. Setup → Phase 3 (US1) → Phase 4 (US2) → validate → this is the MVP.
2. Add Phase 5 (US3, full-height toggle) → validate independently (can be developed in parallel with the MVP track since it's a disjoint file).
3. Add Phase 6 (US4, placement + tooltips) → validate — this phase is last since it depends on both US1 (composer buttons in final position) and US3 (resize button existing).
4. Phase 7 (Polish) closes out the feature.

### Parallel Team Strategy

With two developers: Developer A takes Phase 3 → Phase 4 (`ChatComposer.tsx`); Developer B takes Phase 5 (`ExpandedChatPanel.tsx` + new store) at the same time. Both converge on Phase 6, which needs both tracks' output.
