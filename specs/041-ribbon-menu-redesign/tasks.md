# Tasks: Ribbon Menu Redesign

**Input**: Design documents from `/specs/041-ribbon-menu-redesign/`

**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, quickstart.md ✓

**Tests**: Not explicitly requested — no test tasks generated (per constitution §10: tests are written for new behavior, but this is a visual redesign of existing components without net-new logic paths; existing tests cover the expand/collapse state machine and keyboard behavior).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

---

## Phase 1: Setup — No setup needed

This feature modifies three existing files. No project initialization, new dependencies, or folder structure changes are required.

**Checkpoint**: Begin implementation immediately.

---

## Phase 2: Foundational — Export ExpandDirection type

**Purpose**: The `ExpandDirection` type must exist before `CircularAction` and `WorkspaceOverlay` can both reference it.

- [x] T001 Export `ExpandDirection = 'left' | 'right' | 'up' | 'down'` type from `src/AskLucy.Web/ClientApp/src/components/workspace-shell/CircularAction.tsx`

**Checkpoint**: `ExpandDirection` is importable — US1, US2, US3 can now proceed.

---

## Phase 3: User Story 1 — Right-side button expands horizontal ribbon left (Priority: P1) 🎯 MVP

**Goal**: Right-stack circular buttons (Layers, Navigation, Selection, Analysis, Map style, View mode) expand as a single-row horizontal pill to the LEFT. Trigger turns green. Active option highlighted purple.

**Independent Test**: Open the immersive viewer → click Layers → ribbon pill appears to the left of the trigger in one row → trigger is green → active layer option (if any) is purple → click away → trigger returns to dark gray.

### Implementation for User Story 1

- [x] T002 [US1] In `CircularAction.tsx`: add `expandDirection?: ExpandDirection` prop (default `'down'`), derive `isHorizontal = expandDirection === 'left' || expandDirection === 'right'`, derive `flexDir` (`row-reverse` / `row` / `column-reverse` / `column`), and derive `collapseOrientation` (`horizontal` for left/right, `vertical` for up/down)
- [x] T003 [US1] In `CircularAction.tsx`: update outer `Box` `flexDirection` to use `flexDir`, update `alignItems` to `'center'` for horizontal directions and `'flex-start'` for vertical directions
- [x] T004 [US1] In `CircularAction.tsx`: update `borderRadius` on outer Box — expanded+horizontal → `radius.pill`; expanded+vertical → `radius.lg`; collapsed → `radius.pill` (unchanged)
- [x] T005 [US1] In `CircularAction.tsx`: update `CIRCULAR_ACTION_CHROME.collapsedBg` from `oklch(0.25 0.02 280 / 0.85)` to `#45454D`; update outer Box `bgcolor` to `transparent` when collapsed (Fab now carries the collapsed color explicitly)
- [x] T006 [US1] In `CircularAction.tsx`: update Fab `bgcolor` — collapsed: `#45454D`; expanded: `#2E7F26`; hover: preserve `scale(1.05)` and darken slightly (`#3a6b1f`)
- [x] T007 [US1] In `CircularAction.tsx`: update content `Box` padding — horizontal-left: `py: 1.25, pl: 1.5, pr: 0.5`; horizontal-right: `py: 1.25, pl: 0.5, pr: 1.5`; vertical-down (existing): `px: 1.5, pb: 1.25, pt: 0.5`; vertical-up: `px: 1.5, pt: 1.25, pb: 0.5`
- [x] T008 [US1] In `CircularAction.tsx`: update content `Box` size guard — vertical: keep `width: expanded ? 'auto' : 0`; horizontal: add `height: expanded ? 'auto' : 0` to prevent natural height inflating the pill
- [x] T009 [US1] In `CircularAction.tsx`: pass `orientation={collapseOrientation}` to `<Collapse>`
- [x] T010 [P] [US1] In `ExpandableActionGroup.tsx`: change highlighted action colors — `bgcolor: '#9C62DE'`, `color: '#fff'`, hover `bgcolor: '#7B43C0'` (was `warning.main` amber)
- [x] T011 [US1] In `WorkspaceOverlay.tsx`: import `ExpandDirection` from `CircularAction`, add `PLACEMENT_DIRECTION` map (`'top-cluster' → 'down'`, `'right-stack' → 'left'`, `'bottom-end' → 'up'`), pass `expandDirection={PLACEMENT_DIRECTION[control.placement]}` in `renderControl`

**Checkpoint**: Right-stack buttons show horizontal ribbon to the left. Trigger green when open. Active option purple. Click away/Escape collapses correctly.

---

## Phase 4: User Story 2 — Top button expands ribbon downward (Priority: P1)

**Goal**: Top-cluster `action-group` controls expand downward (direction `'down'`). This is the existing default behavior, but now uses the updated colors (green trigger, purple highlight) because T005/T006/T010 already applied those globally.

**Independent Test**: Open any future top-cluster action-group control → ribbon appears below the trigger → trigger is green → active option is purple.

### Implementation for User Story 2

- [x] T012 [US2] Verify in browser that `top-cluster` placement resolves to `expandDirection='down'` via the `PLACEMENT_DIRECTION` map from T011 and that the existing vertical Collapse behavior is preserved with the new colors

**Checkpoint**: Top-cluster controls (non-Account) expand downward with updated visual treatment.

---

## Phase 5: User Story 3 — Bottom button expands ribbon upward (Priority: P2)

**Goal**: Bottom-end `action-group` controls expand upward (direction `'up'`). The chat panel trigger is a `FloatingPanel` (not `ExpandableActionGroup`) so it is unaffected; this story applies to any future `bottom-end` action-group control.

**Independent Test**: Any `bottom-end` action-group control → ribbon appears above the trigger → trigger is green → active option is purple.

### Implementation for User Story 3

- [x] T013 [US3] Verify `bottom-end` resolves to `expandDirection='up'` via `PLACEMENT_DIRECTION`; confirm `column-reverse` flex + vertical Collapse produces ribbon above the trigger. No code change expected — covered by T011.

**Checkpoint**: Bottom-end action-group controls expand upward.

---

## Phase 6: User Story 4 — Account menu unchanged (Priority: P1)

**Goal**: Account button (top-cluster, `layout="list"`) continues to use the existing icon+label list layout, no ribbon. The `expandDirection='down'` it receives is harmless since `ExpandableActionGroup layout="list"` ignores the ribbon layout path.

**Independent Test**: Click Account → existing list panel appears below → no visual change to Account menu content, border-radius, or padding style.

### Implementation for User Story 4

- [x] T014 [US4] Verify in browser that Account menu still renders its list layout correctly after all changes from T001–T011 are applied — no code change expected

**Checkpoint**: Account menu looks and behaves identically to before.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T015 [P] Check Badge alignment in horizontal ribbon layout — `alignSelf: 'flex-start'` on the Badge may need to be `'center'` for horizontal directions; adjust in `CircularAction.tsx` if the badge dot renders off the trigger circle (applied: `alignSelf: isHorizontal ? 'center' : 'flex-start'`)
- [ ] T016 [P] Verify `border-radius` transition looks clean in horizontal directions (the circle stays `radius.pill` both collapsed and expanded — just different sizes); if the transition looks odd, suppress `border-radius` from the transition list for horizontal directions
- [x] T017 [P] Run `tsc -b --noEmit` in `src/AskLucy.Web/ClientApp` to confirm no TypeScript errors
- [ ] T018 Run quickstart.md validation scenarios manually in the browser (collapsed color, horizontal ribbon, purple highlight, collapse on click-away/Escape, one-at-a-time, Account unchanged)
- [x] T019 Update `docs/RIBBON_MENU.md` status from design doc to implemented — add a one-line note confirming implementation complete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — skip, nothing to do
- **Phase 2 (Foundational)**: No dependencies — do first (T001 unblocks all US phases)
- **Phase 3 (US1)**: Depends on T001 — start here, this is the bulk of the work (T002–T011)
- **Phase 4 (US2)**: Depends on Phase 3 completing — verification only (T012)
- **Phase 5 (US3)**: Depends on Phase 3 completing — verification only (T013)
- **Phase 6 (US4)**: Depends on Phase 3 completing — verification only (T014)
- **Phase 7 (Polish)**: Depends on US1–US4 complete — T015/T016/T017 can be [P]; T018 last

### Within User Story 1

- T002–T009 are sequential changes within `CircularAction.tsx` (same file)
- T010 is independent (different file — `ExpandableActionGroup.tsx`) — can run in parallel with T002–T009
- T011 depends on T001 and T002 being done (needs `ExpandDirection` type + `expandDirection` prop to exist)

### Parallel Opportunities

```bash
# T010 can run alongside any of T002–T009 (different file):
Task: T002–T009  # CircularAction.tsx changes
Task: T010       # ExpandableActionGroup.tsx color change (independent)

# After Phase 3 complete, T012/T013/T014 can run in parallel (verification only):
Task: T012  # verify top-cluster
Task: T013  # verify bottom-end
Task: T014  # verify Account menu

# Polish tasks T015/T016/T017 are independent:
Task: T015  # badge alignment check
Task: T016  # border-radius transition check
Task: T017  # tsc type check
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. T001 — export `ExpandDirection` type
2. T002–T009 — update `CircularAction.tsx`
3. T010 — update `ExpandableActionGroup.tsx` (can overlap with T002–T009)
4. T011 — update `WorkspaceOverlay.tsx`
5. **STOP and VALIDATE**: Open viewer, click Layers — horizontal ribbon to the left, green trigger, purple active option

### Incremental Delivery

1. Phase 2 → Phase 3 (US1) → manual validate in browser → commit
2. Phase 4 + 5 + 6 (verification only, no code expected) → confirm no regressions
3. Phase 7 (polish) → tsc clean, docs updated → final commit

---

## Notes

- [P] = can run in parallel with other [P] tasks in the same phase
- T012, T013, T014 are browser verification tasks, not code changes — they confirm the PLACEMENT_DIRECTION map (T011) correctly covers all placement values
- The Account menu exclusion is architecturally zero-cost: `layout="list"` bypasses the `row` layout path entirely in `ExpandableActionGroup`, so it receives the `expandDirection` prop but ignores it
- Constitution §7 (theming): the chrome colors (`#45454D`, `#2E7F26`, `#9C62DE`) are theme-independent fixed values intentionally outside the MUI theme — consistent with the existing `CIRCULAR_ACTION_CHROME` comment and the readdy.ai reference design pattern already established in the codebase
