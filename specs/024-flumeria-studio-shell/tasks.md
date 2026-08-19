---

description: "Task list for Flumeria Studio Workspace Shell (024-flumeria-studio-shell)"

---

# Tasks: Flumeria Studio Workspace Shell

**Input**: Design documents from `specs/024-flumeria-studio-shell/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/workspace-shell-components.md, quickstart.md

**Tests**: Included. Constitution §10 requires tests for new behavior in the same PR, and this codebase's existing convention pairs every component with a `*.test.tsx` and, where interactive, a `*.a11y.test.tsx` (jest-axe) — this feature follows that convention throughout.

**Organization**: Tasks are grouped by user story (spec.md) to enable independent implementation and testing of each story.

## Path Conventions

Frontend-only feature. All paths are relative to `src/AskLucy.Web/ClientApp/` unless stated otherwise (backend/`src/AskLucy.Web` is untouched by this feature).

---

## Phase 1: Setup

**Purpose**: Scaffolding shared by every later phase.

- [X] T001 [P] Create `src/components/workspace-shell/` directory with `types.ts` defining the `ControlDefinition` type (`id`, `label`, `icon`, `status: 'functional' | 'coming-soon'`, `kind: 'action-group' | 'panel'`) per [data-model.md](./data-model.md)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The store and base control primitive every subsequent story builds on.

**⚠️ CRITICAL**: No user story work in Phase 4 onward can begin until this phase is complete.

- [X] T002 Implement `workspaceOverlayStore` (Zustand, no `persist` middleware) in `src/store/workspaceOverlayStore.ts` — `expandedControlId`, `viewMode`, `unreadControlIds`, `expand(id)`, `collapse()`, `toggle(id)`, `setViewMode(mode)`, `markUnread(id)` per [data-model.md](./data-model.md)
- [X] T003 [P] Unit tests for `workspaceOverlayStore` in `src/store/workspaceOverlayStore.test.ts` (expanding a new id collapses the previous one — FR-015; `expand`/`toggle` clears that id from `unreadControlIds`; no persistence across a fresh store instance)
- [X] T004 Implement `CircularAction` in `src/components/workspace-shell/CircularAction.tsx` per [contracts/workspace-shell-components.md](./contracts/workspace-shell-components.md) — controlled `expanded` prop, `aria-expanded`/`aria-controls`, Enter/Space toggles via `onToggle`, Escape collapses and returns focus to the trigger, expand/collapse driven by `theme.transitions` (no hardcoded duration), optional `badge` dot
- [X] T005 [P] Tests for `CircularAction` in `src/components/workspace-shell/CircularAction.test.tsx` (mouse click and Enter/Space both call `onToggle`; Escape while expanded collapses and refocuses the trigger; `disabled` blocks activation)
- [X] T006 [P] Accessibility test for `CircularAction` in `src/components/workspace-shell/CircularAction.a11y.test.tsx` (jest-axe, zero violations in both collapsed and expanded states)
- [X] T007 Implement `WorkspaceOverlay` in `src/components/workspace-shell/WorkspaceOverlay.tsx` per contract — renders one `CircularAction` per `ControlDefinition` in `controls`, wires `expanded`/`onToggle` from `workspaceOverlayStore` (depends on T002, T004), renders `children` independent of the expand/collapse state machine. Implementation note: internally delegates positioning to a single bottom-end `FloatingToolbar` (built early — see T027/T028/T029 below — since it has zero dependencies of its own and `WorkspaceOverlay` would otherwise need a throwaway parallel layout implementation).
- [X] T008 [P] Tests for `WorkspaceOverlay` in `src/components/workspace-shell/WorkspaceOverlay.test.tsx` (only the control matching `expandedControlId` shows expanded content; `children` render regardless of `expandedControlId`; passing an empty `controls` array renders no controls without error)

**Checkpoint**: Store + base control + coordinating overlay exist and are tested in isolation. User story phases can now begin.

---

## Phase 3: User Story 1 - Arriving in the immersive Studio workspace (Priority: P1) 🎯 MVP

**Goal**: A signed-in user landing on the workspace sees it renamed to "Flumeria Studio" at `/studio`, filling the full viewport, with no permanent toolbar/sidebar/menu — just the placeholder surface, the relocated AI presence card, and every control (including account/session access) reachable through the new circular-control pattern. Old `/chat` links still work.

**Independent Test**: Sign in, navigate to `/studio`, confirm the surface fills the viewport, the tab title reads "Flumeria Studio," no fixed-position chrome is visible, and every account-menu destination (including Log out) is still reachable via the account control; separately navigate to `/chat` and confirm it redirects to `/studio`.

### Implementation for User Story 1

- [X] T009 [US1] In `src/routes/router.tsx`: rename the `/chat` route to `/studio` (still rendering `ChatPage`), and add a `/chat` route rendering `<Navigate to="/studio" replace />` (FR-001/FR-002)
- [X] T010 [P] [US1] Update the internal `/chat` literal to `/studio` in `src/components/AppShell.tsx` (home `RouterLink to`, `isHome` check)
- [X] T011 [P] [US1] Update the internal `/chat` literal to `/studio` in `src/components/ErrorPage.tsx`
- [X] T012 [P] [US1] Update the internal `/chat` literal to `/studio` in `src/routes/AdminRoute.tsx`
- [X] T013 [P] [US1] Update the internal `/chat` literal to `/studio` in `src/routes/PublicOnlyRoute.tsx` and `src/routes/PublicOnlyRoute.test.tsx`
- [X] T014 [P] [US1] Update the internal `/chat` literal to `/studio` in `src/features/auth/pages/LoginPage.tsx`
- [X] T015 [P] [US1] Update the internal `/chat` literal to `/studio` in `src/features/auth/pages/ExternalLoginCompletePage.tsx`
- [X] T016 [P] [US1] Update the internal `/chat` literal to `/studio` in `src/features/landing/components/LandingCtaBar.tsx` and `src/features/landing/components/LandingCtaBar.test.tsx`
- [X] T017 [US1] Create `WorkspaceSurface` (full-viewport neutral placeholder — soft alternating CSS gradient, not a canvas) in `src/features/chat/components/WorkspaceSurface.tsx` per research.md #8
- [X] T018 [P] [US1] Tests for `WorkspaceSurface` in `src/features/chat/components/WorkspaceSurface.test.tsx` (renders full-bleed; no interactive/canvas element mounted)
- [X] T019 [US1] Create `AiPresenceCard` in `src/features/chat/components/AiPresenceCard.tsx`, relocating the existing `SceneBackground`/`ReactiveSphere`/`ParticleSphereBloom` scene and `useVoiceOutput` wiring from full-viewport into a small floating rounded-square card (research.md #7), keeping the scene's existing lazy-loading, and rendering a simple static fallback (e.g. Lucy's static portrait) if the scene fails to load instead of failing silently (constitution §2.VIII; spec.md Edge Cases)
- [X] T020 [P] [US1] Tests for `AiPresenceCard` in `src/features/chat/components/AiPresenceCard.test.tsx` (renders within card bounds; scene import stays lazy-loaded; fallback renders when the scene import rejects)
- [X] T021 [US1] Restructure `src/features/chat/pages/ChatPage.tsx` top level to mount `WorkspaceSurface` → `WorkspaceOverlay` (with `controls={[]}` for now — populated in US2/US3) → `AiPresenceCard` as the overlay's `children`, removing the direct `SceneBackground`/`MinimalTopBar` mount, and set the document title to "Flumeria Studio" (depends on T007, T017, T019)
- [X] T022 [US1] Add an `account` `ControlDefinition` (`kind: 'action-group'`, wrapping the existing `UserMenu` destinations — Profile, Settings, Documents, Knowledge Bases, Memory Center, Prompts, Agents, Workflows, Admin panel, Privacy Policy, Log out — plus the theme-toggle `IconButton`, both reused unchanged) in `src/features/chat/workspaceControls.ts`, and wire it into `ChatPage.tsx`'s `WorkspaceOverlay` (FR-024) — depends on T007, T021
- [X] T023 [P] [US1] Test in `src/features/chat/pages/ChatPage.test.tsx` that every `UserMenu` destination and the theme toggle remain reachable from `/studio` via the account circular control, and that "Log out" still successfully signs the user out
- [X] T024 [P] [US1] Update `src/features/chat/pages/ChatPage.a11y.test.tsx` and `ChatPage.test.tsx` for the restructured shell (zero axe violations; no persistent toolbar landmark present; title assertion)

**Checkpoint**: `/studio` loads, fills the viewport, shows the AI presence card, no permanent chrome, every account/theme destination still reachable, `/chat` redirects. Independently testable and demoable even though no tool/chat controls exist yet.

---

## Phase 4: User Story 2 - Reaching tools through a contextual circular control (Priority: P1)

**Goal**: Every viewer-tool circular control (2D/3D, layers, navigation, selection, analysis) expands in place, shows real content (view mode) or a clearly-labeled "coming soon" placeholder, and only one control is expanded at a time.

**Independent Test**: From `/studio`, activate each of the five tool controls in turn; confirm expand/collapse, "coming soon" placeholders where expected, real 2D/3D switching, and the single-expanded-control invariant.

### Implementation for User Story 2

- [X] T025 [P] [US2] Implement `ExpandableActionGroup` in `src/components/workspace-shell/ExpandableActionGroup.tsx` per contract — `status: 'coming-soon'` renders `comingSoonMessage` with non-interactive actions (FR-012); `status: 'functional'` renders clickable, Tab-reachable actions
- [X] T026 [P] [US2] Tests for `ExpandableActionGroup` in `src/components/workspace-shell/ExpandableActionGroup.test.tsx`
- [X] T027 [P] [US2] Implement `FloatingToolbar` in `src/components/workspace-shell/FloatingToolbar.tsx` per contract (`anchor` positioning; lays out `CircularAction` children, no state of its own) — built early alongside T007 (Foundational), since `WorkspaceOverlay` composes it directly
- [X] T028 [P] [US2] Implement `ContextualToolbar` in `src/components/workspace-shell/ContextualToolbar.tsx` per contract (same rendering contract as `FloatingToolbar`; delegates to it directly — DRY) — built early alongside T027
- [X] T029 [P] [US2] Tests for `FloatingToolbar` and `ContextualToolbar` in `src/components/workspace-shell/FloatingToolbar.test.tsx` and `src/components/workspace-shell/ContextualToolbar.test.tsx`
- [X] T030 [US2] Define the five tool `ControlDefinition`s (`view-mode` functional/action-group, `layers`/`navigation`/`selection`/`analysis` coming-soon/action-group) in `src/features/chat/workspaceControls.ts` per FR-010/FR-012/FR-021
- [X] T031 [US2] Wire the five tool controls into `ChatPage.tsx`'s `WorkspaceOverlay` via a `FloatingToolbar`, replacing the placeholder empty `controls` array from T021 (depends on T007, T021, T025, T027, T030)
- [X] T032 [US2] Implement 2D/3D view-mode switching: selecting a mode in the `view-mode` `ExpandableActionGroup` calls `workspaceOverlayStore.setViewMode`, and `WorkspaceSurface` visibly reflects the active mode (FR-011) — modify `src/features/chat/components/WorkspaceSurface.tsx` and `src/features/chat/workspaceControls.ts`
- [X] T033 [P] [US2] Update `src/features/chat/pages/ChatPage.test.tsx`: expanding one tool control collapses a previously expanded one (FR-015); layers/navigation/selection/analysis show "coming soon"; view-mode switch visibly changes state

**Checkpoint**: All five tool controls reachable and correctly behaved; single-expanded invariant holds across them. Independently testable on top of US1.

---

## Phase 5: User Story 3 - Talking to Lucy without permanent chrome (Priority: P2)

**Goal**: Chat is reached through the same circular-control pattern as every other control, with zero regression to existing chat functionality, and the old bespoke toggle/store are fully retired.

**Independent Test**: Activate the chat control, confirm the familiar conversation panel opens, send a message and confirm it streams as before, collapse it and confirm the rest of the workspace stays usable.

### Implementation for User Story 3

- [X] T034 [US3] Implement `FloatingPanel` in `src/components/workspace-shell/FloatingPanel.tsx` per contract — focus moves inside on open without trapping, stays mounted while collapsed (`inert`/`aria-hidden`, not unmount), reusing the existing glass-panel visual pattern (`createGlassTokens`) from today's `AssistantPanel.tsx`
- [X] T035 [P] [US3] Tests for `FloatingPanel` in `src/components/workspace-shell/FloatingPanel.test.tsx` and `src/components/workspace-shell/FloatingPanel.a11y.test.tsx`
- [X] T036 [US3] Rework `src/features/chat/components/AssistantPanel.tsx` to render as the `children` of a `FloatingPanel` instead of its own absolutely-positioned glass `Box`, keeping `ConversationSwitcher` + `children` (`ConversationView`) content unchanged (depends on T034)
- [X] T037 [US3] Add the `chat` `ControlDefinition` (`kind: 'panel'`) to `src/features/chat/workspaceControls.ts` and wire it into `ChatPage.tsx`'s `WorkspaceOverlay`, replacing the standalone `<AssistantPanel>`/`<AssistantToggleFab>` mount (depends on T007, T021, T034, T036 — touches the same `controls` array as T031/US2; merge trivially, no execution-order dependency on US2's tasks)
- [X] T038 [US3] Delete `src/features/chat/components/AssistantToggleFab.tsx` and `AssistantToggleFab.test.tsx` — superseded by the chat `CircularAction`
- [X] T039 [US3] Delete `src/store/assistantPanelStore.ts` and `assistantPanelStore.test.ts`; replace every remaining read/write (`isOpen`, `toggle`, `markUnread`, `hasUnreadWhileCollapsed`) in `ChatPage.tsx`/`ConversationView` with `workspaceOverlayStore` equivalents (`expandedControlId === 'chat'`, `toggle('chat')`, `markUnread('chat')`, `unreadControlIds.has('chat')`) (depends on T037)
- [X] T040 [P] [US3] Update `src/features/chat/pages/ChatPage.test.tsx` and `ChatPage.a11y.test.tsx`: sending a message through the chat control's `FloatingPanel` streams a response with zero behavior change from pre-redesign (FR-014/SC-006); collapsing chat leaves the rest of the workspace interactive

**Checkpoint**: Chat fully migrated onto the new control system; no dead/parallel toggle code remains. Independently testable on top of US1+US2.

---

## Phase 6: User Story 4 - Operating the workspace without a mouse (Priority: P2)

**Goal**: Every circular control, across all seven (view mode, layers, navigation, selection, analysis, chat, account), is fully reachable, operable, and understandable using only a keyboard and a screen reader.

**Independent Test**: Tab through the assembled `/studio` page, expand/collapse each control with Enter/Space/Escape only, and confirm a screen reader announces expand/collapse state changes.

### Implementation for User Story 4

- [X] T041 [US4] Verify/adjust DOM order in `ChatPage.tsx`/`WorkspaceOverlay.tsx`/`FloatingToolbar.tsx` so `Tab` visits all seven `CircularAction`s in a predictable, visually-matching order (no `tabindex` traps)
- [X] T042 [P] [US4] End-to-end keyboard integration test in `src/features/chat/pages/ChatPage.test.tsx`: Tab through every control, Enter/Space expands, Tab reaches revealed content, Escape collapses and returns focus to the originating trigger — for each of the seven controls
- [X] T043 [P] [US4] Full-assembled-page accessibility test in `src/features/chat/pages/ChatPage.a11y.test.tsx`: jest-axe run against `/studio` in the all-collapsed state and in each single-expanded state, zero violations
- [X] T044 [US4] Close any gap found by T042/T043 (e.g., missing `aria-controls` target id, focus not returning correctly) in the affected component from Phase 2/4/5

**Checkpoint**: Entire workspace verified operable end-to-end via keyboard/screen reader alone, not just per-component in isolation.

---

## Phase 7: User Story 5 - Consistent experience across devices (Priority: P3)

**Goal**: The workspace remains fully usable — surface, controls, and chat panel — at mobile, tablet, and desktop widths.

**Independent Test**: Load `/studio` at mobile, tablet, and desktop viewport widths; confirm the surface still fills the screen and every control remains reachable, legible, and non-overlapping.

### Implementation for User Story 5

- [X] T045 [US5] Add responsive breakpoint behavior to `FloatingToolbar`/`WorkspaceOverlay` (`src/components/workspace-shell/FloatingToolbar.tsx`, `WorkspaceOverlay.tsx`) so controls reposition/resize rather than overlap at mobile widths (FR-020), mirroring `MinimalTopBar`'s existing `xs`/`sm` breakpoint pattern
- [X] T046 [US5] Verify `FloatingPanel` (chat) carries over `AssistantPanel`'s existing mobile full-width/bottom-clearance behavior (`xs`/`sm` sx values) into `src/components/workspace-shell/FloatingPanel.tsx`
- [X] T047 [US5] Verify `AiPresenceCard` positioning doesn't overlap `WorkspaceOverlay` controls at mobile widths in `src/features/chat/components/AiPresenceCard.tsx`
- [X] T048 [P] [US5] Responsive test coverage in `src/features/chat/pages/ChatPage.test.tsx` at mobile/tablet/desktop viewport widths, verifying no overlapping/clipped controls

**Checkpoint**: All five user stories independently functional; workspace usable at every target viewport width.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Whole-feature verification after every story is complete.

- [X] T049 [P] Run `npm run lint` in `src/AskLucy.Web/ClientApp` and fix any violations across new/modified files — clean (the one repo-wide error, in `MemoryExportButton.tsx`, is pre-existing/untouched by this feature)
- [X] T050 [P] Confirm no stray `/chat` literal remains for internal navigation (only the `/chat` → `/studio` redirect route definition itself should reference `/chat`), per quickstart.md's Definition of Done — verified via `grep`
- [X] T051 Browser-verified (headless Chromium via Playwright, mocked backend — no live ASP.NET Core/SQL Server instance in this environment): navigated to `/studio`, screenshotted the collapsed state, Layers expanded ("coming soon"), Chat expanded (full conversation UI), and Account expanded (all destinations + theme toggle + log out) — matching quickstart.md Scenarios 1–3. Found and fixed 2 real rendering bugs invisible to jsdom-based tests (see Notes). NOT covered: real WebGL particle-sphere rendering (headless Chromium showed a plain circle, not the full ReactiveSphere — inconclusive without a GPU-backed browser), voice input/output (needs a live backend + microphone), and genuine mobile-device/touch testing. A human should still spot-check those before shipping.
- [X] T052 Run `npm run test` (full Vitest suite) in `src/AskLucy.Web/ClientApp` and confirm zero regressions across the whole `ClientApp` suite, not just files touched by this feature — 97 files / 354 tests, all passing

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup (T001 for the `ControlDefinition` type). BLOCKS Phases 4–7 (US2–US5), which all consume `CircularAction`/`WorkspaceOverlay`/`workspaceOverlayStore`.
- **User Story 1 (Phase 3)**: Depends only on Foundational (uses `WorkspaceOverlay` with an empty `controls` array, then populates it with the `account` control). Does **not** depend on US2/US3 — independently shippable as the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational + US1 (extends the `WorkspaceOverlay` mounted in T021).
- **User Story 3 (Phase 5)**: Depends on Foundational + US1 (extends the same `WorkspaceOverlay`); independent of US2's tool controls (adds one more `ControlDefinition`, doesn't touch US2's) — see the T037 note above; the two stories' edits to the same `controls` array merge trivially and do not impose an execution-order dependency on each other.
- **User Story 4 (Phase 6)**: Depends on US1+US2+US3 being implemented (it verifies/hardens keyboard behavior across all seven controls together).
- **User Story 5 (Phase 7)**: Depends on US1+US2+US3 (verifies responsive layout of the fully-assembled overlay).
- **Polish (Phase 8)**: Depends on all desired user stories being complete.

### Within Each User Story

- Reusable component + its tests before the `ChatPage.tsx` wiring that consumes it.
- `workspaceControls.ts` additions before the `WorkspaceOverlay` wiring task that references them.
- Deletions of old code (`AssistantToggleFab`, `assistantPanelStore`) only after their replacement is wired in (US3: T038/T039 after T037).

### Parallel Opportunities

- T001 has no dependents blocking it — runs alone first.
- Within Phase 2: T003, T005, T006, T008 (tests) run in parallel with each other once their subject task (T002/T004/T007) is done.
- Within Phase 3 (US1): T010–T016 (seven independent file edits) are fully parallel; T018/T020/T023/T024 (tests) parallel with each other.
- Within Phase 4 (US2): T025, T027, T028 (three independent new components) run in parallel; T026/T029 (tests) parallel with each other.
- Within Phase 5 (US3): T035 tests parallel with T036 once T034 lands.
- US2 (Phase 4) and US3 (Phase 5) can be staffed and built in parallel by different people once US1 (Phase 3) is done — both extend the same `WorkspaceOverlay` but touch disjoint `ControlDefinition` entries and disjoint new files, merging cleanly (only T031 vs. T037's edits to `ChatPage.tsx`'s `controls` array need a trivial merge — see the dependency note on T037).

---

## Parallel Example: Foundational Phase

```bash
# After T002 (workspaceOverlayStore) and T004 (CircularAction) land:
Task: "Unit tests for workspaceOverlayStore in src/store/workspaceOverlayStore.test.ts"
Task: "Tests for CircularAction in src/components/workspace-shell/CircularAction.test.tsx"
Task: "Accessibility test for CircularAction in src/components/workspace-shell/CircularAction.a11y.test.tsx"
```

## Parallel Example: User Story 1

```bash
# The seven /chat -> /studio literal updates touch disjoint files:
Task: "Update /chat to /studio in src/components/AppShell.tsx"
Task: "Update /chat to /studio in src/components/ErrorPage.tsx"
Task: "Update /chat to /studio in src/routes/AdminRoute.tsx"
Task: "Update /chat to /studio in src/routes/PublicOnlyRoute.tsx (+ test)"
Task: "Update /chat to /studio in src/features/auth/pages/LoginPage.tsx"
Task: "Update /chat to /studio in src/features/auth/pages/ExternalLoginCompletePage.tsx"
Task: "Update /chat to /studio in src/features/landing/components/LandingCtaBar.tsx (+ test)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: `/studio` loads full-bleed, renamed, chrome-free, with the AI presence card visible, every account/theme destination reachable, and `/chat` redirects — demoable even with zero tool/chat controls yet.

### Incremental Delivery

1. Setup + Foundational → reusable primitives ready.
2. User Story 1 → renamed, full-bleed shell with account/session access preserved (MVP demo).
3. User Story 2 → viewer-tool controls usable.
4. User Story 3 → chat migrated onto the new system, old code retired.
5. User Story 4 → keyboard/screen-reader hardening across everything built so far.
6. User Story 5 → responsive hardening across everything built so far.
7. Polish → whole-suite regression pass + quickstart validation.

### Parallel Team Strategy

1. One person/pair completes Setup + Foundational (Phases 1–2).
2. Once Foundational is done, User Story 1 (Phase 3) must land first (it's what US2–US5 extend).
3. After US1: Developer A takes US2 (Phase 4), Developer B takes US3 (Phase 5) — both extend the same `WorkspaceOverlay`/`workspaceControls.ts` but touch disjoint entries.
4. US4 (Phase 6) and US5 (Phase 7) start once US2+US3 are both merged, since they verify the fully-assembled set of seven controls.

---

## Notes

- [P] tasks touch different files with no unmet dependency.
- [US#] maps each task to its spec.md user story for traceability.
- No backend/database/API tasks exist — this feature is entirely within `src/AskLucy.Web/ClientApp`.
- `assistantPanelStore.ts` and `AssistantToggleFab.tsx` are deleted outright in US3 (T038/T039), not deprecated in place, per constitution §2.III (no parallel/dead code).
- T051's real-browser pass (headless Chromium) caught two rendering bugs that all 354 passing Vitest tests missed, since jsdom doesn't do real layout — both fixed in `CircularAction.tsx`:
  1. Collapsed controls rendered as pills, not circles: MUI's `Collapse` only animates *height*, not width — its child kept contributing its full intrinsic width (e.g. `ExpandableActionGroup`'s 220–340px `minWidth`) to the outer inline-flex column even at `height: 0`, stretching the trigger's own pill/circle border-radius into a stadium shape at rest. Fixed by forcing the Collapse's child to `width: 0` (with `overflow: hidden`, already present) while collapsed, so only the Fab determines collapsed width.
  2. The expanded trigger's icon was nearly invisible: `color={expanded ? 'primary' : 'default'}` on the `Fab` was paired with a hardcoded `bgcolor: 'transparent'` in `sx`, which defeats MUI's `color="primary"` contrast guarantee (it assumes a solid background to contrast the icon against). Fixed by using `color="inherit"` and setting the icon's own color directly via `sx` (`primary.main` when expanded, `text.primary` when collapsed) instead of relying on Fab's built-in palette/background pairing.
- The `account` control (T022/T023, FR-024) was added after an initial `/speckit-analyze` pass found that removing `MinimalTopBar` without it would have silently dropped the user's only way to log out or navigate elsewhere from this page.
- Commit after each task or logical group; stop at any checkpoint to validate a story independently.
