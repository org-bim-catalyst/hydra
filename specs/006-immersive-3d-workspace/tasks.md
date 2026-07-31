---

description: "Task list template for feature implementation"
---

# Tasks: Immersive 3D AI Workspace

**Input**: Design documents from `/specs/006-immersive-3d-workspace/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/README.md](./contracts/README.md), [quickstart.md](./quickstart.md)

**Tests**: Not explicitly requested in spec.md, but constitution §10/§18 requires tests for
new/changed observable behavior in the same PR — included in Phase 7 (Polish) rather than
per-story TDD gating, since this feature ships as one cohesive branch.

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P1/P2/P3) to
enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4)
- All paths are relative to `src/AskLucy.Web/ClientApp/src/` unless stated otherwise

## Path Conventions

Existing single web application: ASP.NET Core backend (`src/AskLucy.*`, untouched by this
feature — see plan.md Constitution Check) + React/Vite frontend at
`src/AskLucy.Web/ClientApp/src/`. All tasks below touch only the frontend, primarily
`features/chat/`.

---

## Phase 1: Setup

**Purpose**: Add the new 3D rendering dependencies this feature needs

- [X] T001 Add `three`, `@react-three/fiber`, `@react-three/drei`, and `simplex-noise` to `src/AskLucy.Web/ClientApp/package.json` and install (research.md "Summary of new dependencies")

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared infrastructure every user story builds on — new files only, nothing
existing is modified yet, so these are safe to parallelize

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 [P] Create glassmorphism theme tokens (light/dark backdrop-blur + translucent surface colors) in `theme/tokens/glass.ts` (research.md §6)
- [X] T003 [P] Create `assistantPanelStore.ts` Zustand store — `isOpen`, `hasUnreadWhileCollapsed`, `open()`, `close()`, `toggle()`, `markUnread()`, `persist` middleware, following the existing `store/themeStore.ts` pattern — in `store/assistantPanelStore.ts` (data-model.md "AssistantPanelState")
- [X] T004 [P] Create `useSceneQualityTier.ts` hook — WebGL2 feature detection, `prefers-reduced-motion` via `matchMedia`, viewport-breakpoint check, exposing a `'full' | 'reduced' | 'static-fallback'` tier plus a setter for later dynamic downgrade — in `features/chat/scene/useSceneQualityTier.ts` (research.md §4, data-model.md "SceneQualityTier")

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Immersive arrival on the workspace (Priority: P1) 🎯 MVP

**Goal**: A full-viewport, continuously rotating 3D sphere renders behind all UI on
`/chat` and responds to manual drag/zoom/pan, without depending on the assistant panel
or conversation switcher existing yet.

**Independent Test**: Load `/chat`; confirm the sphere fills the viewport, sits behind
all other content, rotates on its own, and responds to drag/scroll/pinch.

### Implementation for User Story 1

- [X] T005 [US1] Restructure `features/chat/pages/ChatPage.tsx`'s root layout from the current flex(sidebar + column) container to a full-viewport layered container (`position: relative`, full-height) with an empty placeholder full-bleed background `<div>` — this only prepares the layer; the actual `<SceneBackground>` import/mount happens in T008 once that component exists, not here (depends on T004)
- [X] T006 [P] [US1] Write the noise-based vertex-displacement shaders `features/chat/scene/sphere.vert.glsl` and `features/chat/scene/sphere.frag.glsl` (research.md §2)
- [X] T007 [US1] Create `features/chat/scene/ReactiveSphere.tsx` — `IcosahedronGeometry` + `drei` `shaderMaterial` using T006's shaders, auto-rotating idle animation via `useFrame`, exposing `amplitude`/`frequency` props for later reactive wiring (depends on T006)
- [X] T008 [US1] Create `features/chat/scene/SceneBackground.tsx` — R3F `<Canvas>` + `OrbitControls` (rotate/zoom/pan enabled, damped), `React.lazy`/`Suspense`-loaded, mounting `<ReactiveSphere>` only when `useSceneQualityTier` reports a tier other than `'static-fallback'` — and replace T005's placeholder `<div>` in `ChatPage.tsx` with the real `<SceneBackground>` import (depends on T004, T005, T007)
- [X] T009 [US1] Wrap `<SceneBackground>` in a component-scoped error boundary so a WebGL init/render failure degrades gracefully instead of crashing the page (constitution §2.VIII) in `features/chat/scene/SceneBackground.tsx` (depends on T008)
- [X] T010 [US1] Mark the canvas container `aria-hidden`/non-focusable as decorative content (FR-014) in `features/chat/scene/SceneBackground.tsx` (depends on T008)

**Checkpoint**: User Story 1 is fully functional and testable independently — the
existing chat UI still works underneath/around the scene, unrestyled.

---

## Phase 4: User Story 2 - Chat with the floating assistant (Priority: P1) 🎯 MVP

**Goal**: The fixed chat column is replaced by a floating, collapsible glassmorphism
assistant panel with a persistent round toggle; the sphere visibly reacts while the
assistant speaks a reply. Brand/account/theme controls are preserved in a minimal bar
outside the panel (FR-015) rather than silently absorbed into it.

**Independent Test**: Open the panel via the toggle, send a message, see the streamed
response legible over the moving background; collapse the panel; trigger a spoken reply
and confirm the sphere deforms while it plays; confirm brand/theme/account controls
remain reachable outside the panel in both its open and collapsed states.

### Implementation for User Story 2

- [X] T011 [US2] Create `features/chat/components/AssistantPanel.tsx` — MUI `Paper`/`Box` using the T002 glass tokens, left-anchored fixed position, `Slide`/`Collapse` expand/collapse bound to `assistantPanelStore`, hosting only the chat-specific controls from `ConversationView` (`LanguageSelector`, Translate, Generate Image, message list, composer) — not brand/account/theme (depends on T002, T003)
- [X] T012 [P] [US2] Create `features/chat/components/AssistantToggleFab.tsx` — persistent round MUI `Fab` bound to `assistantPanelStore.isOpen`/`toggle()` (depends on T003)
- [X] T013 [US2] Update `features/chat/pages/ChatPage.tsx` to render `<AssistantPanel>` and `<AssistantToggleFab>` in place of the old fixed sidebar+column flex layout, keeping `<SceneBackground>` as the full-bleed layer behind both (depends on T005, T011, T012)
- [X] T014 [US2] Create `features/chat/components/MinimalTopBar.tsx` — a thin persistent bar (`BrandMark`, theme toggle, `UserMenu`) rendered outside `AssistantPanel`, over the 3D scene, satisfying FR-015's "minimal navigation outside the panel" (depends on T005)
- [X] T015 [US2] Update `features/chat/pages/ChatPage.tsx` to mount `<MinimalTopBar>` alongside `<AssistantPanel>`/`<AssistantToggleFab>`, removing the old AppBar's brand/theme/account controls from `ConversationView` now that they live in `MinimalTopBar` (depends on T011, T013, T014)
- [X] T016 [US2] Extend `features/chat/voice/useTextToSpeech.ts` to expose `isSpeaking`/`intensity` (a damped envelope driven by `onstart`/`onboundary`/`onend` events) alongside a caller-visible `onerror` path, without changing its existing `speak`/`stop`/`isSupported` contract (research.md §3; closes the pre-existing no-silent-failure gap noted in plan.md's Constitution Check)
- [X] T017 [US2] Wire `ReactiveSphere`'s `amplitude` prop to the `isSpeaking`/`intensity` value from T016 so the sphere deforms while the assistant speaks and returns to idle at `onend`/`onerror`, in `features/chat/scene/ReactiveSphere.tsx` and `features/chat/pages/ChatPage.tsx` (depends on T007, T016)
- [X] T018 [US2] Implement the FR-016 unread indicator: call `assistantPanelStore.markUnread()` when an assistant message completes while `isOpen` is `false`; render a `Badge` on `AssistantToggleFab`; clear it on open, in `features/chat/pages/ChatPage.tsx` and `features/chat/components/AssistantToggleFab.tsx` (depends on T003, T012, T013)

**Checkpoint**: User Stories 1 and 2 together deliver the core immersive-arrival +
working-chat experience (the practical MVP), with FR-015's minimal navigation covered.

---

## Phase 5: User Story 3 - Switch between conversations from the panel (Priority: P2)

**Goal**: The permanent conversation-history sidebar is replaced by a compact selector
inside the assistant panel, reusing the existing chat-list hooks unchanged.

**Independent Test**: With 2+ existing conversations, open the selector at the top of
the panel, pick one, confirm its full history loads; with zero conversations, confirm
the empty/start-new state.

### Implementation for User Story 3

- [X] T019 [US3] Extract `features/chat/components/ChatSidebar.tsx`'s search/filter/sort/list/row-action logic into a reusable inner list component, decoupled from its current fixed 300px column shell — the extracted list keeps its `useVirtualizer` wiring, and the component accepts an explicit bounded-height container from its caller (needed for T020's Popover) rather than assuming a full-height column (research.md §7)
- [X] T020 [US3] Create `features/chat/components/ConversationSwitcher.tsx` — a compact control at the top of the panel that opens a MUI `Popover`/`Menu` with a fixed max-height, hosting the T019 list inside that bounded scroll container so `useVirtualizer` continues to work, closes on selection, and shows an empty/start-new state when there are no conversations (depends on T019)
- [X] T021 [US3] Update `features/chat/components/AssistantPanel.tsx` to render `<ConversationSwitcher>` at its top instead of the old permanent `<ChatSidebar>` column, and remove `ChatPage.tsx`'s now-unused mobile `Drawer`-based sidebar handling, in `features/chat/components/AssistantPanel.tsx` and `features/chat/pages/ChatPage.tsx` (depends on T011, T013, T020)

**Checkpoint**: All three stories work together — conversation history is fully usable
from inside the floating panel; the permanent sidebar is retired.

---

## Phase 6: User Story 4 - Consistent experience across devices (Priority: P3)

**Goal**: The workspace adapts across desktop/tablet/mobile, degrades gracefully on
low-end hardware, falls back cleanly without WebGL, and respects reduced-motion.

**Independent Test**: Resize across breakpoints; disable WebGL and reload; enable
`prefers-reduced-motion`; throttle CPU — confirm graceful, non-broken behavior in each
case (quickstart.md scenarios 9–12).

### Implementation for User Story 4

- [X] T022 [US4] Wire `drei`'s `PerformanceMonitor` in `features/chat/scene/SceneBackground.tsx` to call `useSceneQualityTier`'s downgrade setter on sustained frame-time regression (research.md §4) (depends on T004, T008)
- [X] T023 [US4] Implement `'reduced'`-tier behavior in `features/chat/scene/ReactiveSphere.tsx` — lower subdivision/vertex count and/or paused idle rotation when tier is not `'full'` (depends on T007, T022)
- [X] T024 [US4] Implement the static-fallback themed gradient background shown when the tier is `'static-fallback'` or the T009 error boundary catches, ensuring `AssistantPanel` remains fully usable, in `features/chat/scene/SceneBackground.tsx` (depends on T004, T009)
- [X] T025 [P] [US4] Apply MUI responsive breakpoints to `features/chat/components/AssistantPanel.tsx`, `features/chat/components/AssistantToggleFab.tsx`, and `features/chat/components/MinimalTopBar.tsx` (mobile vs. tablet/desktop positioning/sizing), verifying no overlap, clipping, or unreachable controls (depends on T011, T012, T014)
- [X] T026 [US4] Respect `prefers-reduced-motion` in `features/chat/scene/ReactiveSphere.tsx`/`features/chat/scene/useSceneQualityTier.ts` — freeze idle rotation and cap reactive-deformation amplitude when the media query matches, regardless of tier (depends on T004, T007, T017)

**Checkpoint**: All four user stories are independently functional; the spec's full
requirement set is covered.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Requirements that span multiple stories, plus the test coverage constitution
§10/§18 requires for the behavior introduced above

- [X] T027 [P] Implement FR-021/SC-011 loading sequencing in `features/chat/scene/SceneBackground.tsx` — synchronous CSS gradient placeholder, cross-fade to the canvas on R3F's `onCreated` ready callback
- [X] T028 [P] Verify/complete keyboard focus order and visible focus states across `AssistantPanel`, `AssistantToggleFab`, `MinimalTopBar`, `ConversationSwitcher`, and `ChatComposer` (FR-013)
- [X] T029 [P] Unit tests for `assistantPanelStore` transitions in `store/assistantPanelStore.test.ts`
- [X] T030 [P] Unit tests for `useSceneQualityTier`'s tier-selection matrix (WebGL/reduced-motion/breakpoint combinations) in `features/chat/scene/useSceneQualityTier.test.ts` (also satisfies constitution §10's performance-test obligation for FR-020/SC-010 — see plan.md Constitution Check for why a CI-blocking GPU frame-rate test isn't used instead)
- [X] T031 [P] Unit tests for `useTextToSpeech`'s `isSpeaking`/`intensity` envelope and `onerror` path in `features/chat/voice/useTextToSpeech.test.ts`
- [X] T032 [P] Component test for `ConversationSwitcher` (reusing the existing `ChatSidebar` hook mocks), including a case with enough items to verify virtualization still works inside its bounded Popover, in `features/chat/components/ConversationSwitcher.test.tsx`
- [X] T033 [P] Component test for `AssistantToggleFab`'s unread indicator show/clear in `features/chat/components/AssistantToggleFab.test.tsx`
- [X] T034 Automated `jest-axe` pass on the redesigned `/chat` route, covering both panel-open and panel-collapsed states, extending `features/chat/pages/ChatPage.a11y.test.tsx`
- [ ] T035 Run quickstart.md's 15 validation scenarios end-to-end and record results
  - Automated substitute completed: `tsc -b` (0 errors), `eslint .` (0 errors, 2 pre-existing
    `react-hooks/incompatible-library` warnings unrelated to this feature), full `vitest run`
    (all tests pass except 4 pre-existing, unrelated `AdminUsersPage.test.tsx` failures — a
    missing-Router-wrapper bug that predates this branch), and `npm run build` (succeeds;
    `SceneBackground` confirmed as its own ~905KB lazy chunk, separate from the main and
    `ChatPage` bundles, per constitution §15/research.md §5).
  - **Not done**: the 15 scenarios themselves require a real browser (visual check of the
    sphere, drag/zoom/pan feel, devtools WebGL/reduced-motion/CPU-throttle emulation) — no
    browser automation tool was available in this session. Recommend running quickstart.md
    manually before merge.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational only
- **User Story 2 (Phase 4)**: Depends on Foundational; T013 depends on US1's T005 (shared root layout), and T014 also depends on T005 — otherwise independent of US1's scene internals
- **User Story 3 (Phase 5)**: Depends on Foundational; T021 depends on US2's T011/T013 (needs `AssistantPanel` to exist as the mounting point)
- **User Story 4 (Phase 6)**: Depends on Foundational; individual tasks depend on the specific US1/US2 files they refine (T022/T023/T024 on the scene from US1; T025 on the panel/toggle/top-bar from US2; T026 on both)
- **Polish (Phase 7)**: Depends on all four user stories being complete

### User Story Dependencies

- **US1 (P1)**: No dependency on other stories — independently testable as soon as Foundational is done
- **US2 (P1)**: Builds on US1's root layout (T005) to place the panel over the scene, but its own panel/voice/toggle logic is independent
- **US3 (P2)**: Builds on US2's `AssistantPanel` as its mounting point; the extracted list logic (T019) itself has no story dependency
- **US4 (P3)**: Refines US1's scene and US2's panel for device/performance/accessibility conditions — apply last, after both exist

### Parallel Opportunities

- T002, T003, T004 (all of Foundational) run in parallel — different new files, no cross-dependency
- T006 (shaders) can run in parallel with T005 (layout restructuring) within US1
- T012 (toggle Fab) can run in parallel with T011 (panel shell) within US2; T014 (MinimalTopBar) can also run in parallel with T011/T012 once T005 is done
- T025 (responsive breakpoints) can run in parallel with T022–T024 (perf/fallback tiers) within US4
- Most of Phase 7 (T027–T033) can run in parallel — separate test files and independent concerns; T034/T035 run last since they validate the integrated whole

---

## Parallel Example: Foundational Phase

```bash
Task: "Create glassmorphism theme tokens in theme/tokens/glass.ts"
Task: "Create assistantPanelStore.ts Zustand store in store/assistantPanelStore.ts"
Task: "Create useSceneQualityTier.ts hook in features/chat/scene/useSceneQualityTier.ts"
```

## Parallel Example: User Story 1

```bash
Task: "Restructure ChatPage.tsx root layout in features/chat/pages/ChatPage.tsx"
Task: "Write sphere.vert.glsl and sphere.frag.glsl in features/chat/scene/"
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (blocks everything)
3. Complete Phase 3: User Story 1 — **STOP and VALIDATE**: sphere fills the viewport, idles, responds to manual manipulation
4. Complete Phase 4: User Story 2 — **STOP and VALIDATE**: floating panel opens/closes, chat works, sphere reacts to voice
5. Both P1 stories together are the practical MVP — deploy/demo here if ready

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. US1 → validate independently → demo (scene only, old sidebar/chat still visible around it)
3. US2 → validate independently → demo (MVP: immersive arrival + working floating chat)
4. US3 → validate independently → demo (conversation history restored via the panel)
5. US4 → validate independently → demo (robust across devices/hardware/accessibility settings)
6. Polish → full test coverage + quickstart validation → ship

---

## Notes

- [P] tasks touch different files with no dependency between them
- [Story] labels trace every task back to its spec.md user story
- Commit after each task or logical group
- Stop at any checkpoint to validate a story independently before moving on
- Per constitution §10/§18, the Phase 7 tests are not optional polish to be deferred to a
  follow-up — they land in the same PR as the behavior they cover
