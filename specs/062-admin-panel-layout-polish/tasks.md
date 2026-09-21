---

description: "Task list for Admin Panel Layout & Polish Pass"
---

# Tasks: Admin Panel Layout & Polish Pass

**Input**: Design documents from `/specs/062-admin-panel-layout-polish/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: No test tasks were explicitly requested for this feature; per constitution §10, test updates are still required for changed behavior and are folded directly into each story's implementation tasks (component and a11y tests alongside the component they cover), rather than a separate "write failing tests first" phase.

**Organization**: Tasks are grouped by user story (US1-US5, per spec.md priorities) so each can ship and be verified independently.

## Path Conventions

Single web-frontend project. All paths are relative to `src/AskLucy.Web/ClientApp/src/`.

---

## Phase 1: Setup

No project-initialization tasks are needed — this feature only edits/adds files inside the existing, already-configured `AskLucy.Web/ClientApp` project (existing Vite/TypeScript/Vitest tooling, MUI, TanStack Query already installed).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared admin shell layout fix (US2) and the `AdminNavItem` type change (part of US5) are infrastructure every other page-level story assumes is in place for a visually correct result. US2 is also independently a P1 story in its own right, so it is implemented as the first story below rather than a separate phase — no additional foundational work is required beyond it.

**Checkpoint**: None beyond Phase 3 (US2) — proceed directly to User Story 1, which has no dependency on US2.

---

## Phase 3: User Story 1 - Readable provider health at a glance (Priority: P1) 🎯 MVP

**Goal**: Split the "Possibly out of date" staleness chip out of the AI Providers Health column into its own column so it never wraps or overlaps.

**Independent Test**: Open Admin → AI providers with a stale provider; confirm the staleness indicator renders in its own column, never wraps into the Health cell, and its tooltip never overlaps another row.

### Implementation for User Story 1

- [X] T001 [US1] Extract the staleness `Chip`+`Tooltip` (and its `isStale` computation) out of `features/admin/components/ProviderHealthCell.tsx` into a new `features/admin/components/ProviderStalenessCell.tsx`, taking the same `provider` prop shape; `ProviderHealthCell.tsx` keeps only the health status chip
- [X] T002 [US1] In `features/admin/pages/AdminAiProvidersPage.tsx`, add a new table column header (e.g. "Last confirmed") immediately after the Health column header, and render `<ProviderStalenessCell provider={provider} />` in the corresponding cell for each row
- [X] T003 [US1] Update `features/admin/components/ProviderHealthCell.test.tsx` to drop staleness-chip assertions (now out of scope for this component) and add `features/admin/components/ProviderStalenessCell.test.tsx` covering: stale provider shows the chip+tooltip, non-stale provider shows nothing, never-checked provider (`healthStaleAfterUtc` null) shows nothing
- [X] T004 [US1] Update `features/admin/pages/AdminAiProvidersPage.a11y.test.tsx` and any snapshot/row-shape assertions in this page's other tests to account for the new column; additionally assert, at a narrow container width, that the staleness cell's tooltip does not overlap the following row's content (covers FR-002/SC-001, which require no overlap "at any supported viewport width")

**Checkpoint**: AI Providers page shows staleness in its own column; `ProviderHealthCell` and `ProviderStalenessCell` tests pass independently.

---

## Phase 4: User Story 2 - Full-height admin pages (Priority: P1)

**Goal**: Every admin page's sidebar and active tab content stretch to fill the full viewport height, matching the Account Settings page pattern.

**Independent Test**: Open several admin pages at varying viewport heights; confirm the sidebar and tab content both extend to fill available height with no dead space, and each scrolls independently when its content overflows.

### Implementation for User Story 2

- [X] T005 [US2] In `features/admin/components/AdminShell.tsx`, change the outer row `Box`'s `alignItems: 'flex-start'` to `alignItems: 'stretch'`
- [X] T006 [US2] In `features/admin/components/AdminShell.tsx`, give the content slot `Box` (currently `sx={{ flex: 1, minWidth: 0 }}`) the additional `display: 'flex', flexDirection: 'column', minHeight: 0` so it can pass stretch context down to whatever page renders inside it
- [X] T007 [P] [US2] In `features/admin/pages/AdminDefaultModelsPage.tsx`, wrap the page root in `flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column'` and wrap the `TableContainer` in a `flex: 1, minHeight: 0, overflow: 'auto'` container, mirroring the `Paper`+`Box` idiom in `features/settings/pages/SettingsPage.tsx`
- [X] T008 [P] [US2] Apply the same root/table flex wiring from T007 to `features/admin/pages/AdminAiCapabilitiesPage.tsx`
- [X] T009 [P] [US2] Apply the same root flex wiring from T007 to the remaining admin pages that render primary list/table content (`features/admin/pages/AdminUsersPage.tsx`, `AdminRolesPage.tsx`, `AdminRoleAssignmentsPage.tsx`, `AdminSystemAgentsPage.tsx`, `AdminAiProvidersPage.tsx`, `features/workflows/components/WorkflowPolicyAdminPanel.tsx`'s host page, `features/agents/components/AgentPolicyAdminPanel.tsx`'s host page, `features/mcp/pages/McpAdministrationPage.tsx`) so full-height stretching is consistent across every admin section
- [X] T010 [US2] Update `features/admin/components/AdminShell.test.tsx` and `features/admin/components/AdminShell.a11y.test.tsx` to assert the stretched layout (e.g. content container's computed flex styles), that the content container has independent `overflow: auto` scroll while the sidebar nav remains fully visible/reachable (covers FR-004), that the collapsed-sidebar breakpoint still stretches both panes to the shorter viewport's height (covers the responsive note in research.md Decision 2), and confirm no new axe violations
- [X] T011 [P] [US2] Update `features/admin/pages/AdminDefaultModelsPage.test.tsx` for the new layout wrapper structure (selectors/queries that may depend on DOM nesting)
- [X] T011a [P] [US2] Add a regression assertion (in `features/admin/pages/AdminUsersPage.test.tsx` or `AdminUsersPage.a11y.test.tsx`, a hint-less page) confirming its layout/DOM structure is unaffected by the US2/US3 flex changes (covers FR-006)

**Checkpoint**: All admin pages fill the viewport height and scroll independently, matching Account Settings; AdminShell tests pass. Independently testable/shippable even without US1.

---

## Phase 5: User Story 3 - Hint banners anchored to the bottom (Priority: P2)

**Goal**: On admin pages with an info/hint banner, the table stretches to fill height and the hint sits with zero gap directly beneath it, just above the page bottom.

**Independent Test**: Open Default models (short table) and AI Capabilities; confirm the table renders first, stretches to fill available height, and the hint sits flush beneath it regardless of row count.

**Dependency**: Builds on the flex wiring from US2 (T007, T008) — implement after Phase 4.

### Implementation for User Story 3

- [X] T012 [US3] In `features/admin/pages/AdminDefaultModelsPage.tsx`, reorder JSX so the `TableContainer` (already `flex: 1, minHeight: 0, overflow: 'auto'` from T007) renders before the `Alert` hint; set `mb: 0` on the table wrapper and `mt: 0` on the `Alert` so there is no gap between them
- [X] T013 [US3] Apply the same reorder + zero-gap spacing from T012 to `features/admin/pages/AdminAiCapabilitiesPage.tsx`
- [X] T014 [P] [US3] Update `features/admin/pages/AdminDefaultModelsPage.test.tsx` to assert the hint renders after the table in DOM order
- [X] T015 [P] [US3] Add/update an equivalent DOM-order assertion for `features/admin/pages/AdminAiCapabilitiesPage.tsx` (create `AdminAiCapabilitiesPage.test.tsx` if none exists, scoped only to this assertion plus existing smoke coverage if any)

**Checkpoint**: Hint banners sit flush under their tables with no gap on both known pages, verified even when the table is short.

---

## Phase 6: User Story 4 - Policy creation moved into a modal (Priority: P2)

**Goal**: Replace the inline "New Policy" forms on Workflow Policies and Agent Policies with a button + modal (MCP servers pattern), freeing the table to use the full page height.

**Independent Test**: Open Workflow Policies (then Agent Policies); confirm no inline form, a button opens a modal with the same fields/validation, submit creates the policy and closes the modal, cancel discards input, and the table now fills the page height.

**Dependency**: Benefits from US2's flex wiring (T009 already covers these two pages) but is independently testable on its own.

### Implementation for User Story 4

- [X] T016 [P] [US4] Create `features/workflows/components/WorkflowPolicyFormDialog.tsx` — a MUI `Dialog` mirroring `features/mcp/components/McpServerForm.tsx`'s prop shape (`open`, `isSaving`, `errorMessage`, `onClose`, `onSubmit`), containing the Name, Node Type, Underlying Tool Name, Description, and Conditions JSON fields currently inline in `WorkflowPolicyAdminPanel.tsx`, with the same `canCreate` validation gating the submit action
- [X] T017 [US4] In `features/workflows/components/WorkflowPolicyAdminPanel.tsx`, remove the inline "New Policy" `Paper` section, add a header `Stack` with title + "New policy" `Button` (mirroring `features/mcp/components/McpServerList.tsx`'s header row) that opens `WorkflowPolicyFormDialog`, wire the existing `createPolicy` mutation into the dialog's `onSubmit`, and let the `TableContainer` grow to fill the freed height
- [X] T018 [P] [US4] Create `features/agents/components/AgentPolicyFormDialog.tsx` — same pattern as T016 but with Agent Policies' field set (Name, Tool Name, Description, Conditions JSON)
- [X] T019 [US4] Apply the same panel refactor as T017 to `features/agents/components/AgentPolicyAdminPanel.tsx`, wiring in `AgentPolicyFormDialog`
- [X] T020 [P] [US4] Add `features/workflows/components/WorkflowPolicyFormDialog.test.tsx` (dialog open/submit/cancel behavior) and `features/workflows/components/WorkflowPolicyAdminPanel.test.tsx` (new file — no prior panel test exists; covers: button opens dialog, valid submit calls the create mutation and closes the dialog with the new row visible, cancel closes without creating a policy)
- [X] T021 [P] [US4] Add `features/agents/components/AgentPolicyFormDialog.test.tsx` and `features/agents/components/AgentPolicyAdminPanel.test.tsx` (new file — no prior panel test exists), mirroring T020's coverage for Agent Policies

**Checkpoint**: Both policy pages create via modal only, table occupies full height, and modal open/submit/cancel behavior is covered by tests.

---

## Phase 7: User Story 5 - System Agents relocated in the sidebar (Priority: P3)

**Goal**: Move "System Agents" to immediately after "Role assignments" in the admin sidebar, with a divider separating that pair from the rest of the nav.

**Independent Test**: Open the admin panel; confirm "System Agents" appears directly after "Role assignments," a divider follows it, and clicking it still navigates correctly with unchanged permission gating.

### Implementation for User Story 5

- [X] T022 [US5] Add an optional `dividerAfter?: boolean` field to the `AdminNavItem` interface in `features/admin/adminNav.tsx`
- [X] T023 [US5] In `features/admin/adminNav.tsx`, reorder `ADMIN_NAV` so "System agents" is immediately after "Role assignments," and set `dividerAfter: true` on the "System agents" entry (depends on T022)
- [X] T024 [US5] In `features/admin/components/AdminShell.tsx`'s nav `.map()`, render a MUI `Divider` immediately after any item whose `dividerAfter` is `true` (depends on T022)
- [X] T025 [P] [US5] Update `features/admin/components/AdminShell.test.tsx` to assert the new nav order and the presence of exactly one divider, positioned after "System agents"
- [X] T026 [P] [US5] Update `features/admin/components/AdminShell.a11y.test.tsx` to confirm the divider introduces no new axe violations and keyboard tab order still flows correctly across it

**Checkpoint**: Sidebar order and divider match spec; all other nav items' routes/permissions unchanged.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final verification across all five stories together.

- [ ] T027 Run the full `quickstart.md` validation pass across all five user stories in a real browser session (short and tall viewports)
- [X] T028 [P] Run the full frontend test suite (`npm test` in `AskLucy.Web/ClientApp`) and fix any cross-story regressions surfaced by page-level tests (e.g. `AdminAiProvidersPage.a11y.test.tsx` re-checked against both the new staleness column and the full-height layout together)
- [X] T029 [P] Run `tsc -b --noEmit` (not bare `tsc --noEmit` — project references require `-b`) to confirm no type errors from the `AdminNavItem.dividerAfter` addition or the new dialog components

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup**: None.
- **US1 (Phase 3)**: No dependency on any other story — can be done first, last, or in parallel with everything else.
- **US2 (Phase 4)**: No dependency on other stories, but its `AdminShell.tsx` and per-page flex changes are a prerequisite for US3 (T012/T013 build on T007/T008) and are recommended (not required) before US4's table-height payoff is visible.
- **US3 (Phase 5)**: Depends on US2's per-page flex wiring (T007, T008) already being in place.
- **US4 (Phase 6)**: Independently testable on its own; benefits from US2's T009 flex wiring for the "table fills full height" payoff but the modal conversion itself has no hard dependency.
- **US5 (Phase 7)**: Fully independent of every other story.
- **Polish (Phase 8)**: Depends on all stories being complete.

### Recommended Implementation Order

Given the dependency shape above: **US2 → US3 → US1 / US4 / US5 (any order/parallel)**. US2 first maximizes payoff for US3 and US4; US1 and US5 can genuinely be done at any point since they touch disjoint files.

### Parallel Opportunities

- T001-T004 (US1) can run entirely in parallel with T022-T026 (US5) — disjoint files.
- Within US2, T007, T008, T009, T011, T011a are marked [P] — each touches a different page/test file.
- Within US4, T016 and T018 (the two new dialog components) are marked [P] — disjoint files; T020 and T021 (their tests) are likewise [P].
- T028 and T029 (Phase 8) can run in parallel with each other once T027's manual pass is underway.

---

## Parallel Example: User Story 2

```bash
# Launch the per-page flex-wiring tasks together (different files, same pattern):
Task: "Apply full-height flex wiring to AdminDefaultModelsPage.tsx"
Task: "Apply full-height flex wiring to AdminAiCapabilitiesPage.tsx"
Task: "Apply full-height flex wiring to the remaining admin list/table pages"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 3 (US1): staleness column split.
2. **STOP and VALIDATE**: confirm no overlap on AI Providers page across viewport widths.
3. Ship — US1 is a fully self-contained bug fix independent of every other story.

### Incremental Delivery

1. US1 (staleness column) → ship.
2. US2 (full-height shell) → ship — unlocks the visual payoff for US3 and US4.
3. US3 (hint anchoring) → ship.
4. US4 (policy modals) → ship.
5. US5 (sidebar reorder) → ship — can also be pulled forward earlier since it has zero dependencies.

### Notes

- No new backend/API work in any phase — all tasks are frontend-only per plan.md's Structure Decision.
- Every implementation task that changes user-visible or DOM-structural behavior has a paired test-update task in the same phase, per constitution §10 (tests land with the behavior change, not after).
- Commit after each task or logical group per story; each phase checkpoint is an independently shippable increment.
