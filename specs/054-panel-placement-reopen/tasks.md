# Tasks: Clear-Area Panel Placement & Reopen Tray

**Input**: Design documents from `/specs/054-panel-placement-reopen/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included — constitution §10 requires tests for new/changed behavior in the same change; this repo's existing panel-system files each carry a sibling `*.test.tsx`/`*.test.ts`, and interactive UI additionally carries a `*.a11y.test.tsx` (jest-axe).

**Organization**: Tasks are grouped by user story (spec.md: US1 = P1 clear-area placement, US2 = P2 reopen tray) so each can be implemented and validated independently.

All paths are relative to `src/AskLucy.Web/ClientApp/src/`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 or US2; omitted for Foundational/Polish tasks

---

## Phase 1: Setup

**No tasks.** This is a frontend-only addition to the existing `viewer/panels/` feature domain (constitution §4 folder convention) — no new dependency (research D1), no build/tooling change, no project scaffolding. Work starts at Foundational.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The one shared type surface both user stories build on.

**⚠️ CRITICAL**: T001 blocks every task below it.

- [X] T001 Add `Rect` interface, `ArrangementMode` type (`'grid' | 'cascade'`), and a `manuallyPlaced: boolean` field (default `false`) to `FloatingPanel` in `viewer/panels/types/panel.ts` (data-model.md "Rect", "Arrangement Mode", "FloatingPanel (modified)")

**Checkpoint**: `Rect`/`ArrangementMode`/`manuallyPlaced` exist and compile — both user stories can now proceed.

---

## Phase 3: User Story 1 - Panels open where they can actually be seen and used (Priority: P1) 🎯 MVP

**Goal**: Panels opening without an explicit position land in a non-overlapping grid when they fit, fall back to a size-ordered cascade when they don't, never cover known page/viewer chrome, respect a panel the user has manually moved, offer an explicit "arrange" action, and show a snap-to placeholder while dragging.

**Independent Test**: With the standard page chrome and one overlay widget on screen, open panels one at a time from Lucy/an extension. Two or three panels tile side by side; opening enough panels flips the layout to a cascade with the smallest panel frontmost; dragging a panel shows a landing ghost that disappears over occupied space and snaps the panel on drop; a manually-moved panel is left alone when another panel opens, until the "arrange" action is used. No panel or toolbar button is ever hidden behind another. See quickstart.md Scenarios 1-4, 6.

### Tests for User Story 1

> Write these first; `arrangement.ts`/`reservedRegions.ts` don't exist yet, so they fail until the implementation tasks land.

- [X] T002 [P] [US1] Write `viewer/panels/layout/arrangement.test.ts` covering contracts/arrangement.md requirements A1-A11 (grid packing, cascade fallback + smallest-in-front z-order, pinned panels treated as obstacles, empty-input and fully-covered-host edge cases, deterministic output, A11 cascade anchors at the largest clear region rather than an arbitrary corner) and S1-S5 for `findCandidateSlots`/`slotAtPoint`
- [X] T003 [P] [US1] Write `viewer/panels/layout/reservedRegions.test.ts` covering contracts/reserved-regions.md requirements C1-C5 (host-relative normalization via stubbed `getBoundingClientRect`, zero-size rect rejection, no-throw when `document` yields no matches)

### Implementation for User Story 1

- [X] T004 [US1] Implement `computeArrangement`, `findCandidateSlots`, and `slotAtPoint` (pure, no DOM/store imports) in `viewer/panels/layout/arrangement.ts` per contracts/arrangement.md — first-fit-decreasing shelf packing (D3), cascade fallback with area-descending z-order (D4), so T002 passes (depends on T001)
- [X] T005 [US1] Implement `RESERVED_ATTRIBUTE` constant and `collectReservedRects(hostRect)` in `viewer/panels/layout/reservedRegions.ts` per contracts/reserved-regions.md, so T003 passes (depends on T001)
- [X] T006 [US1] In `viewer/panels/store/floatingPanelStore.ts`: add `applyArrangement(positions, zOrder)` and `arrangeAll()` actions (arrangeAll clears every panel's `manuallyPlaced` flag first, per FR-005e/D5); set `manuallyPlaced: true` inside the existing `updatePosition`/`updateSize` actions only (the user-gesture call sites — `clampToViewport` must NOT set it, per D5) (depends on T004)
- [X] T007 [US1] Extend `viewer/panels/store/floatingPanelStore.test.ts`: `manuallyPlaced` set by `updatePosition`/`updateSize`, untouched by `clampToViewport`, cleared by `arrangeAll`; `applyArrangement` writes positions/z-order without touching `manuallyPlaced`; a `kind: 'content'` panel and a `kind: 'live'` panel of the same size are placed identically by the arrangement pass (FR-012) (depends on T006)
- [X] T008 [US1] In `viewer/panels/components/FloatingPanelHost.tsx`: on panel-count change and on the existing resize handler, call `collectReservedRects` + `computeArrangement` (passing every non-minimized panel, `manuallyPlaced` ones flagged so they become obstacles per D5) and apply the result via `applyArrangement`; run this after `clampToViewport` on resize (FR-005) (depends on T005, T006)
- [X] T008a [US1] Write `viewer/panels/components/FloatingPanelHost.test.tsx` covering: opening a panel triggers `collectReservedRects` + `computeArrangement` + `applyArrangement` over the resulting panel set; closing a panel re-triggers arrangement over the remaining panels; a window resize re-triggers arrangement after `clampToViewport` (FR-002, FR-005, SC-005) (depends on T008)
- [X] T009 [US1] Create `viewer/panels/components/LandingPlaceholder.tsx` — a non-interactive ghost outline rendered at a given `Rect`, styled from the MUI theme (dashed border, low-opacity fill), themed for light/dark (constitution §7) (depends on T001)
- [X] T010 [US1] Wire drag-time placement guidance: `FloatingPanelHost.tsx` holds the transient drag-session state (data-model.md "Drag Session") and renders `LandingPlaceholder` at `activeSlot`; `FloatingPanel.tsx`'s `Rnd` gains `onDragStart` (compute `findCandidateSlots` once, lift up), keeps calling `onDrag`/`onDragStop` but now also resolves `slotAtPoint` per move and, on drop, snaps to the active slot if one exists (D6, FR-005f/g) (depends on T004, T009)
- [X] T011 [US1] Extend `viewer/panels/components/FloatingPanel.test.tsx` and `viewer/panels/components/FloatingPanelHost.test.tsx` (created in T008a) covering: placeholder appears over free space and disappears over an obstacle (FR-005g), drop snaps to the shown slot, drop with no slot shown leaves the panel exactly where released (depends on T008a, T010)
- [X] T012 [US1] Create `viewer/panels/components/PanelDock.tsx` — a left-edge, vertically-centered rail carrying `data-panel-reserved` and a single "Arrange" action calling `arrangeAll()`; renders `null` when zero panels are open (FR-007/FR-008 scoped to this story's slice — the reopen list is added in US2); mount it in `FloatingPanelHost.tsx` (depends on T006)
- [X] T013 [P] [US1] Write `viewer/panels/components/PanelDock.test.tsx` — renders nothing with no open panels, renders the arrange action once a panel is open, clicking it calls `arrangeAll`
- [X] T014 [P] [US1] Write `viewer/panels/components/PanelDock.a11y.test.tsx` — no axe violations, arrange action is a labeled, keyboard-focusable button
- [X] T015 [US1] In `components/workspace-shell/WorkspaceOverlay.tsx`, add `data-panel-reserved` to each `FloatingToolbar`'s `pointerEvents:'auto'` wrapper (top-cluster, right-stack, bottom-end) — not the outer `inset:0` Box (contracts/reserved-regions.md R1)
- [X] T016 [US1] In `viewer/extensions/components/ExtensionToolbar.tsx`: add `data-panel-reserved` (so panels avoid it) and a new `useAvoidReservedCorner` hook (`viewer/extensions/components/useAvoidReservedCorner.ts`) that replaces the hardcoded `top: {xs:460, sm:480}` stopgap with a measured offset below whatever else occupies the top-right corner (WorkspaceOverlay's chrome), falling back to the natural `top: 16` margin when nothing else is there (D9 — implementation note: a pure hardcoded-number removal reintroduced the original WorkspaceOverlay-vs-ExtensionToolbar collision the stopgap existed to fix, since `data-panel-reserved` only governs floating-panel placement, not chrome-vs-chrome layout; this dynamic measurement is the correct in-scope fix)
- [X] T017 [US1] In `features/solar/components/CameraAttitudeWidget.tsx`: add `data-panel-reserved` + reuse `useAvoidReservedCorner`, stacked one fixed toolbar-row below its own WorkspaceOverlay-avoidance offset (not a second dynamic measurement against `ExtensionToolbar`, to avoid a same-commit effect-ordering race between the two — see `useAvoidReservedCorner`'s doc and the shared `CORNER_CHROME_ATTRIBUTE` marker); `CameraAttitudeWidget.test.tsx` needed no changes (D9)

**Checkpoint**: User Story 1 is fully functional and independently testable — grid/cascade placement, manual-pin respect, explicit arrange, drag placeholder, and both stopgaps retired.

---

## Phase 4: User Story 2 - Reopening a panel the user closed (Priority: P2)

**Goal**: Closing a panel moves it into a bounded, session-scoped reopen tray instead of discarding it; the tray is silent when empty; reopening restores the panel via the exact same validation/placement path as a fresh open.

**Independent Test**: Open a panel, close it, confirm it appears in the dock's list, click it, confirm it reopens with original title/content placed by the same clear-area logic. Confirm the list is absent with nothing closed, and that minimizing (not closing) never adds an entry. See quickstart.md Scenario 5, 7.

### Tests for User Story 2

- [X] T018 [P] [US2] Extend `viewer/panels/store/floatingPanelStore.test.ts`: `closePanel` moves the panel into `closedPanels` as a reconstructed `PanelRequest` + `closedAtUtc`; the array caps at `MAX_CONCURRENT_PANELS` (10) dropping the oldest; closing a panel whose id already has an entry replaces it, not duplicates it; `reopenPanel(id)` removes the entry and calls `openPanel` with the stored request; `minimizePanel` never adds an entry; reopen behaves identically for a `kind: 'content'` panel and a `kind: 'live'` panel (FR-006, FR-010, FR-011, FR-012, Edge Cases)

### Implementation for User Story 2

- [X] T019 [US2] Add `ClosedPanelEntry` type (`request: PanelRequest`, `closedAtUtc: number`) to `viewer/panels/types/panel.ts` (data-model.md "Reopen Tray Entry") (depends on T001)
- [X] T020 [US2] In `viewer/panels/store/floatingPanelStore.ts`: add `closedPanels: ClosedPanelEntry[]` state; rewrite `closePanel` to reconstruct the panel's original `PanelRequest` (requestId/kind/title/chrome/contextAssociation, plus content or typeKey+data) and unshift it, capped and dedup'd per T018; add `reopenPanel(id)` that splices the entry out and calls the existing `openPanel(request)` (D8, FR-006, FR-009, FR-010), so T018 passes (depends on T019)
- [X] T021 [US2] Extend `viewer/panels/components/PanelDock.tsx` (built in US1) to render the `closedPanels` list below the arrange action — title + click-to-reopen calling `reopenPanel`; the component now renders `null` only when both `panels` and `closedPanels` are empty (FR-007/FR-008) (depends on T012, T020)
- [X] T022 [P] [US2] Extend `viewer/panels/components/PanelDock.test.tsx` and `PanelDock.a11y.test.tsx`: closed-panel entries render and are clickable/keyboard-operable, clicking calls `reopenPanel`, still renders nothing with both lists empty (depends on T021)
- [X] T023 [US2] Add a regression case (in `floatingPanelStore.test.ts` or a new `FloatingPanel.test.tsx` case) proving FR-013: closing a panel with a valid `contextAssociation`, invalidating its associated layer, then reopening from the tray yields `contextStatus: 'invalid'` — the same indicator an already-open panel would show (depends on T020)

**Checkpoint**: Both user stories work independently and together — the full feature is functional.

---

## Phase 5: Polish & Cross-Cutting Concerns

- [X] T024 [P] Update the panels section of `viewer/README.md` to describe the arrangement engine, reserved-region convention, and reopen tray (constitution §13)
- [X] T025 Run the full validation pass from quickstart.md's "Automated validation": `npx vitest run` (1272/1272 passed, 223/223 files — including the 3 that timed out flakily in an earlier baseline run, confirming those were pre-existing environment flakiness, not a regression from this feature), `npx tsc -b --noEmit` (clean), `npx eslint src/viewer/panels src/components/workspace-shell src/features/solar src/viewer/extensions src/viewer/store` (0 errors, 2 pre-existing warnings in untouched files)
- [ ] T026 Execute quickstart.md's manual browser Scenarios 1-7 against a running app with a location set; append an "Actual outcomes" section to quickstart.md recording the result of each, mirroring spec 053's convention

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: None — no tasks.
- **Foundational (Phase 2)**: T001 only. Blocks every task in Phase 3 and Phase 4.
- **User Story 1 (Phase 3)**: Depends on Foundational. No dependency on User Story 2.
- **User Story 2 (Phase 4)**: Depends on Foundational. Also depends on User Story 1's `PanelDock.tsx` (T012) and `openPanel`'s placement path (T004/T008) already existing — reopened panels are placed by the same arrangement logic (FR-009), and the dock component US2 extends is the one US1 creates. This is a real, spec-driven dependency (not an artificial one), so implement US1 before US2.
- **Polish (Phase 5)**: Depends on both user stories being complete.

### Within Each User Story

- Tests before the implementation that makes them pass (T002/T003 before T004/T005; T018 before T019/T020).
- Pure logic (`arrangement.ts`, `reservedRegions.ts`) before the store/component code that calls it.
- Store actions before the components that dispatch them.

### Parallel Opportunities

- T002 and T003 (different files, both pre-implementation tests).
- T013 and T014 (different test files, same target component, after T012).
- T016 and T017 (different files, independent chrome components) — both depend only on T012/T015 being conceptually established (in practice, only on T001; they can start as soon as the `data-panel-reserved` convention is fixed, i.e., in parallel with T004-T011).
- T022 (single task, no further split needed).
- T024 is independent of T025/T026 and can run any time after both stories land.

---

## Parallel Example: User Story 1

```bash
# Tests, launched together:
Task: "Write viewer/panels/layout/arrangement.test.ts (T002)"
Task: "Write viewer/panels/layout/reservedRegions.test.ts (T003)"

# Once T012/T001 are in, these three are independent:
Task: "Add data-panel-reserved to WorkspaceOverlay.tsx (T015)"
Task: "Add data-panel-reserved to ExtensionToolbar.tsx, drop stopgap (T016)"
Task: "Add data-panel-reserved to CameraAttitudeWidget.tsx, drop stopgap (T017)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 2: Foundational (T001).
2. Complete Phase 3: User Story 1 (T002-T017, plus T008a).
3. **STOP and VALIDATE**: run quickstart.md Scenarios 1-4 and 6 manually; confirm both stopgaps are gone (`grep` check in quickstart.md).
4. This alone fixes the reported live bug (panels/toolbar buried behind chrome) — a legitimate stopping point if reopen isn't needed yet.

### Incremental Delivery

1. Foundational → User Story 1 → validate → (optional deploy point).
2. Add User Story 2 (T018-T023) → validate Scenario 5/7 → full feature complete.
3. Phase 5 polish (README, full suite, manual quickstart run) closes the change out per constitution §19 Definition of Done.

---

## Notes

- [P] tasks touch different files with no unfinished dependency between them.
- Every store/component task names its exact file, matching plan.md's Project Structure.
- No task introduces a new npm dependency (research D1).
- No backend task exists — this feature has zero API/DB surface (plan.md Technical Context).
- Commit after each task or logical group; stop at either checkpoint to validate that story independently before continuing.
