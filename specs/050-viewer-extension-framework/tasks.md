---

description: "Task list for 050-viewer-extension-framework"
---

# Tasks: Viewer Extension Framework

**Input**: Design documents from `/specs/050-viewer-extension-framework/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included. Constitution §10 requires tests for new behaviour in the same change, and spec FR-035 makes the *existing* specs/028, specs/038, specs/042 and `ViewerSurface` suites the primary regression guard for this feature.

**Organization**: Grouped by user story so each is independently implementable and testable.

---

## Decisions this task list encodes (settled 2026-09-12)

- **The viewer-embedded toolbar is built** (FR-021 stands as written). It is a distinct surface from the workspace overlay: extension-contributed controls belong to the viewer and live and die with it, while the workspace overlay sits outside the viewer, controls page UI alongside it, and drives it through the published API. **`ChatPage` and `WorkspaceOverlay` are not touched by this feature** (research D6, spec Clarifications 2026-09-12).
- **FR-019 is struck.** Contributions are store data and hosts are subscribers, so an early contribution is never lost without any notification mechanism. FR-018 and FR-020 still hold and are tested (research D3).

---

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Paths are repository-relative.

## Path Conventions

- Framework: `src/AskLucy.Web/ClientApp/src/viewer/extensions/`
- Migrated capabilities' components (unchanged): `src/AskLucy.Web/ClientApp/src/features/viewer/components/`
- Panels: `src/AskLucy.Web/ClientApp/src/viewer/panels/`

---

## Phase 1: Setup

**Purpose**: Scaffolding for the one new area this feature introduces

- [X] T001 [P] Create directory `src/AskLucy.Web/ClientApp/src/viewer/extensions/` with `store/`, `components/` and `builtin/` subdirectories
- [X] T002a **Capture the viewer startup baseline before anything changes** (SC-007) — time from opening the workspace to the map being interactive, on the current `main`. Record the figure and the method in `specs/050-viewer-extension-framework/quickstart.md` Scenario 1. Once the migration lands there is no baseline left to measure, so this cannot be deferred to the Polish phase where its comparison (T051c) lives
- [X] T002 Confirm React 19 Strict Mode is active in development (`src/AskLucy.Web/ClientApp/src/main.tsx`) and record the finding in `specs/050-viewer-extension-framework/research.md` under D9 — double-invoked effects are the most likely way the loader's idempotency (FR-008) gets exercised in practice, so the design must be verified against the mode that actually runs, not assumed

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The contract, registry, store, context, loader and overlay host. Nothing in any user story can be built until these exist.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T003 Define the extension contract and manifest types (`ViewerExtension`, `ExtensionManifest`) in `src/AskLucy.Web/ClientApp/src/viewer/extensions/ViewerExtension.ts`, per contracts/viewer-extension.md — a plain object produced by a factory, never a base class to extend (constitution §IV, research D1)
- [X] T004 [P] Define the lifecycle state and contribution types (`LifecycleState`, `Contribution` and its four kinds) in `src/AskLucy.Web/ClientApp/src/viewer/extensions/ViewerExtension.ts` per data-model.md
- [X] T005 Implement `viewerExtensionStore` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/store/viewerExtensionStore.ts` — per-extension lifecycle state plus live contributions, session-scoped with no persistence, matching the `viewerEngineStore`/`floatingPanelStore` convention (research D2)
- [X] T006 [P] Unit tests for `viewerExtensionStore` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/store/viewerExtensionStore.test.ts` — state transitions, contributions recorded and removed per extension id
- [X] T007 Implement the extension registry in `src/AskLucy.Web/ClientApp/src/viewer/extensions/registry.ts` — `register` (throwing on a duplicate id in development, mirroring `panelTypeRegistry`'s posture), `resolve` returning `undefined` for an unknown id (FR-006, FR-007, research D9)
- [X] T008 [P] Unit tests for the registry in `src/AskLucy.Web/ClientApp/src/viewer/extensions/registry.test.ts` — register/resolve, duplicate id is a configuration error, unknown id resolves to `undefined` rather than throwing
- [X] T009 Implement the extension context factory in `src/AskLucy.Web/ClientApp/src/viewer/extensions/context.ts` per contracts/extension-context.md — passes the existing `viewerEngine` through unchanged (FR-012), and records every contribution against the calling extension's id so teardown never depends on the author remembering (FR-014, research D7)
- [X] T010 [P] Unit tests for the context in `src/AskLucy.Web/ClientApp/src/viewer/extensions/context.test.ts` — each helper records its contribution against the right extension; `on()` records its unsubscribe; an extension never needs to pass its own id
- [X] T011 Implement the loader in `src/AskLucy.Web/ClientApp/src/viewer/extensions/loader.ts` — start/stop by id, async start bounded by a timeout, per-extension failure containment recording state and reason, and the idempotency rules from data-model.md's Validation Summary (FR-008, FR-010, FR-011, FR-029, research D4, D9). **Pick the timeout value here** and record the reasoning in a comment; research D4 leaves it deliberately open, and nothing in this feature exercises the async path
- [X] T012 [P] Unit tests for the loader in `src/AskLucy.Web/ClientApp/src/viewer/extensions/loader.test.ts` — start-when-started and stop-when-not-started are no-ops producing no duplicate contributions; stop-while-starting wins and discards in-flight contributions; a throwing start is contained with the reason recorded; a start exceeding the timeout is treated exactly as a throw
- [X] T013 Implement `ExtensionOverlayHost` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/components/ExtensionOverlayHost.tsx` — renders every contributed overlay by subscribing to the store, which is what makes a contribution made before the host mounted appear anyway rather than being discarded (FR-020, research D3)
- [X] T014 [P] Unit tests for `ExtensionOverlayHost` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/components/ExtensionOverlayHost.test.tsx` — renders contributed overlays, drops them when withdrawn, and renders one contributed *before* the host mounted
- [X] T015 Create the declared extension set in `src/AskLucy.Web/ClientApp/src/viewer/extensions/declared.ts`, initially empty, with the ordering note from data-model.md (order is for predictable sequencing and stable control order only — no extension may depend on another having started)

**Checkpoint**: The framework exists and is tested in isolation, with nothing migrated onto it yet

---

## Phase 3: User Story 1 — Viewer Capabilities Load as Independent Extensions (Priority: P1) 🎯 MVP

**Goal**: The four capabilities `ViewerSurface` mounts become extensions, with behaviour indistinguishable from today.

**Independent Test**: Open the viewer and exercise every migrated capability — panels (drag, resize, minimise/restore, close, focus/stacking, opacity, cascade, eviction, error panels), POI markers, the site boundary highlight and its confidence badge — confirming no user-visible difference, while `ViewerSurface` references no capability.

**⚠️ This story carries all of the feature's regression risk.** The load-bearing evidence is the *existing* suites continuing to pass, not the new tests below.

### Implementation for User Story 1

- [X] T016 [US1] Implement the panels extension in `src/AskLucy.Web/ClientApp/src/viewer/extensions/builtin/panelsExtension.tsx` — contributes `FloatingPanelHost`, and owns `useFloatingPanelHub` plus the "Reconnecting…" indicator that `ViewerSurface` renders from its `isLive` today (research D8). The hook holds the SignalR connection, so the extension must keep it mounted for exactly as long as it is started
- [X] T017 [US1] Register the panels extension and add `viewer.panels` to `DECLARED_EXTENSIONS` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/declared.ts`
- [X] T018 [US1] Mount `ExtensionOverlayHost` and start the declared set from `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx`, removing its `FloatingPanelHost`, `useFloatingPanelHub` and reconnecting-Chip references. Leave the location-to-map wiring exactly as it is — that is viewer behaviour, not a capability, and it stays until specs/051 (research D10)
- [X] T019 [US1] Run the full specs/028 panel suites plus `ChatPage.test.tsx` and confirm they pass unchanged — panels are the first migration and the one with the most existing coverage, so this is the checkpoint that proves the contract works before three more capabilities move onto it
- [X] T020 [P] [US1] Implement the POI marker extension in `src/AskLucy.Web/ClientApp/src/viewer/extensions/builtin/poiMarkerExtension.tsx` — contributes the existing `POIMarkerOverlay` component unchanged; `stop()` is empty because the overlay was contributed through the context (contracts/viewer-extension.md)
- [X] T021 [P] [US1] Implement the boundary confidence extension in `src/AskLucy.Web/ClientApp/src/viewer/extensions/builtin/boundaryConfidenceExtension.tsx` — contributes the existing `SiteBoundaryConfidenceBadge` component unchanged
- [X] T022 [US1] Register both and add `viewer.poi-marker` and `viewer.boundary-confidence` to the declared set, removing `POIMarkerOverlay` and `SiteBoundaryConfidenceBadge` from `ViewerSurface.tsx`
- [X] T023 [US1] Run the specs/038 POI coverage and the `SiteBoundaryConfidenceBadge` test and a11y suites; confirm unchanged
- [X] T024 [US1] **Migrate the site boundary overlay last** (FR-033) — implement `src/AskLucy.Web/ClientApp/src/viewer/extensions/builtin/siteBoundaryExtension.tsx` contributing the existing `SiteBoundaryOverlay` component unchanged, register it, add `viewer.site-boundary` to the declared set, and remove it from `ViewerSurface.tsx`. This capability has the most post-release history (specs/042 bug-fix rounds, the specs/044 regression); its `handle.setSiteBoundary(null)` cleanup path is exactly what specs/044 touched, so leave the component itself untouched
- [X] T025 [US1] Run `SiteBoundaryOverlay.test.tsx` and the full specs/042 coverage; confirm unchanged
- [X] T026 [US1] Update `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.test.tsx` for the host shape, keeping every existing assertion about WebGL fallback, placeholder, map transition, layer state and revert-to-placeholder intact
- [X] T027 [US1] Verify by inspection that `ViewerSurface.tsx` references no individual capability (quickstart Scenario 2) — the grep in that scenario must return no matches

**Checkpoint**: All four capabilities run as extensions; the viewer names none of them; every pre-existing suite still passes

---

## Phase 4: User Story 2 — A New Capability Ships Without Touching the Core (Priority: P2)

**Goal**: Adding a capability costs its own module plus one declaration line — nothing else.

**Independent Test**: Add a capability contributing an overlay, a live panel kind and a control; confirm all three appear with no edit to the viewer core, the panel framework or any other capability; remove it from the declared set and confirm every trace is gone.

### Implementation for User Story 2

- [X] T028 [US2] Add `registerLivePanelKind` to the context in `src/AskLucy.Web/ClientApp/src/viewer/extensions/context.ts`, registering into specs/049's `panelTypeRegistry` and recording the withdrawal so `unregister` runs on stop
- [X] T029 [US2] Handle a live panel kind withdrawn while a panel of that kind is open, in `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.ts` — the open panel is closed or shown as unavailable rather than left rendering against a capability that no longer exists (FR-036)
- [X] T030 [P] [US2] Unit tests for live panel kind contribution and withdrawal in `src/AskLucy.Web/ClientApp/src/viewer/extensions/context.test.ts` and `src/AskLucy.Web/ClientApp/src/viewer/panels/store/floatingPanelStore.test.ts`
- [X] T031 [US2] Add `openPanel` to the context in `src/AskLucy.Web/ClientApp/src/viewer/extensions/context.ts` — deliberately *not* tracked for teardown, because a panel the user can close is theirs, not the extension's (contracts/extension-context.md)
- [X] T032 [P] [US2] Unit test in `src/AskLucy.Web/ClientApp/src/viewer/extensions/context.test.ts` confirming a panel opened by an extension survives that extension stopping
- [X] T033 [US2] Contain and surface an exception thrown by an extension's viewer-event handler, inside the tracked `on()` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/context.ts` — other subscribers must still receive the event (FR-017)
- [X] T033a [P] [US2] Unit test for event-handler containment in `src/AskLucy.Web/ClientApp/src/viewer/extensions/context.test.ts` — one extension's throwing handler does not stop another extension's handler receiving the same event, and the failure is surfaced rather than swallowed (FR-017, constitution §2.VIII)
- [X] T034 [US2] Define the `ToolbarEntry` type and add `contributeToolbarEntry` to the context in `src/AskLucy.Web/ClientApp/src/viewer/extensions/context.ts`, recording the entry as a withdrawable contribution (FR-013, FR-014, research D6). This targets the **viewer-embedded** toolbar built in T035 — not `WorkspaceOverlay`, which this feature does not touch
- [X] T035 [US2] Implement `ExtensionToolbar` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/components/ExtensionToolbar.tsx` — renders contributed entries from the extension store inside the viewer container, and renders nothing (not an empty broken frame) when no entry has been contributed (FR-021, FR-023)
- [X] T036 [US2] Mount `ExtensionToolbar` in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx`, positioned so it does not collide with the existing viewer-area overlays — the weather widget and boundary confidence badge occupy top-left, the panel-hub indicator bottom-left
- [X] T037 [US2] Give contributed toolbar entries a defined, stable order in `src/AskLucy.Web/ClientApp/src/viewer/extensions/store/viewerExtensionStore.ts` when more than one extension contributes (FR-022)
- [X] T038 [P] [US2] Unit tests for `ExtensionToolbar` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/components/ExtensionToolbar.test.tsx` — entries render, disappear when their extension stops, appear in stable order across two extensions, and the toolbar renders nothing when empty (FR-022, FR-023)
- [X] T038a [P] [US2] Accessibility test in `src/AskLucy.Web/ClientApp/src/viewer/extensions/components/ExtensionToolbar.a11y.test.tsx` — a contributed entry is keyboard reachable and operable with a visible focus state, and the toolbar is readable in both themes (FR-024)
- [X] T039 [US2] Write the extensibility-proof test in `src/AskLucy.Web/ClientApp/src/viewer/extensions/extensibility.test.tsx` — a scratch extension contributing an overlay, a toolbar entry and a live panel kind appears in full and leaves no trace when removed (quickstart Scenario 3, SC-003)
- [X] T039a [P] [US2] Prove the contract accepts a **new contribution kind** without existing extensions changing (FR-005) in `src/AskLucy.Web/ClientApp/src/viewer/extensions/extensibility.test.tsx` — add a throwaway kind to the context and store, and confirm all four migrated extensions still compile and behave unchanged. This is the property specs/051 is built on when it adds scene access, frame callbacks and declared renderer state; it is cheap to assert now and expensive to discover broken later

**Checkpoint**: A capability can be added and removed without the core noticing

---

## Phase 5: User Story 3 — Capability Failures Are Visible and Contained (Priority: P2)

**Goal**: Every lifecycle failure reaches the user, names the capability, and leaves everything else working.

**Independent Test**: Force a start failure, a stop failure, an unknown declared id and a hung start; confirm each produces a visible, understandable outcome while the viewer and all other capabilities stay fully usable.

### Implementation for User Story 3

- [X] T040 [US3] Implement `ExtensionFailureNotice` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/components/ExtensionFailureNotice.tsx` — names the unavailable capabilities using its manifest `displayName`, reusing the Chip pattern `ViewerSurface` already uses for `panel-hub-connection-status` rather than introducing a notification system (research D5)
- [X] T041 [US3] Mount `ExtensionFailureNotice` in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx`
- [X] T042 [US3] Surface an unknown declared id through the same notice, from the loader in `src/AskLucy.Web/ClientApp/src/viewer/extensions/loader.ts`, without preventing the remaining declared extensions starting (FR-013)
- [X] T043 [US3] Surface a stop failure and continue stopping the rest, in `src/AskLucy.Web/ClientApp/src/viewer/extensions/loader.ts` (FR-012, FR-030)
- [X] T044 [P] [US3] Unit tests for failure isolation in `src/AskLucy.Web/ClientApp/src/viewer/extensions/loader.test.ts` — one extension's start failure leaves every other started; one extension's stop failure leaves every other stopped; an unknown declared id does neither
- [X] T045 [P] [US3] Unit tests for `ExtensionFailureNotice` in `src/AskLucy.Web/ClientApp/src/viewer/extensions/components/ExtensionFailureNotice.test.tsx` — renders nothing when nothing failed, names each failed capability when something did
- [X] T046 [P] [US3] Accessibility test in `src/AskLucy.Web/ClientApp/src/viewer/extensions/components/ExtensionFailureNotice.a11y.test.tsx` — readable in both themes and announced, not purely visual (§7)
- [X] T047 [US3] Confirm no lifecycle failure is console-only, by inspection across `loader.ts` and `context.ts` (FR-031, constitution §2.VIII) — every recorded failure must also reach the notice

**Checkpoint**: Every failure path in the spec's User Story 3 produces a visible outcome

---

## Phase 6: User Story 4 — A Toggleable Capability Turns On and Off (Priority: P3)

**Goal**: The activate/deactivate axis exists and works, distinct from started/stopped.

**Independent Test**: With a capability declaring itself toggleable, activate and deactivate it and confirm its effect appears and disappears while it stays started throughout.

**Note**: No migrated capability uses this. It is built now because adding a second lifecycle axis after extensions exist would change the contract for every implementer — see plan.md Complexity Tracking, and re-examine here whether it still costs only the optional method pair.

### Implementation for User Story 4

- [X] T048 [US4] Implement `activate`/`deactivate` in the loader in `src/AskLucy.Web/ClientApp/src/viewer/extensions/loader.ts`, tracking the active/inactive axis separately from lifecycle state — a stopped extension is neither (data-model.md)
- [X] T049 [US4] Reject activating a non-toggleable extension visibly rather than silently ignoring it, in `src/AskLucy.Web/ClientApp/src/viewer/extensions/loader.ts` (FR-004)
- [X] T050 [P] [US4] Unit tests in `src/AskLucy.Web/ClientApp/src/viewer/extensions/loader.test.ts` — a toggleable extension reports itself active after activation and inactive after deactivation while staying started; a non-toggleable one rejects activation visibly

**Checkpoint**: Both lifecycle axes work and are tested

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T051 Stop every running extension and withdraw every contribution when the viewer closes, in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx` (FR-028)
- [X] T051a [P] Unit test for viewer close in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.test.tsx` — unmounting the viewer stops every running extension and leaves no contribution in the store (FR-028)
- [X] T051b [P] Unit test for normal start/stop isolation in `src/AskLucy.Web/ClientApp/src/viewer/extensions/loader.test.ts` — starting or stopping one extension leaves every other extension's contributions and behaviour untouched (FR-016). Distinct from T044, which covers isolation of *failures*; this covers the ordinary case, which is the one that runs constantly
- [X] T051c Measure viewer startup **after** this feature and compare against the baseline captured in T002a (SC-007) — same method, same machine. Record both figures in `specs/050-viewer-extension-framework/quickstart.md` Scenario 1. The requirement is that the viewer becomes usable **no later** than before; extensions start without being awaited as a group (T011), so a regression here means that property was lost somewhere
- [X] T052 [P] Verify repeated start/stop cycles accumulate nothing — no duplicated controls, no orphaned subscriptions, no store growth — in `src/AskLucy.Web/ClientApp/src/viewer/extensions/loader.test.ts` (SC-004, quickstart Scenario 5). This is the requirement the reference implementation's own samples fail, so assert it rather than assuming it
- [X] T053 [P] Update `src/AskLucy.Web/ClientApp/src/viewer/README.md` with an `extensions/` section covering the contract, the context, the contribution kinds and the declared set
- [X] T054 Confirm the spec and implementation still agree on the two-toolbar distinction — that nothing built in T034–T038 touches `ChatPage`, `WorkspaceOverlay` or `components/workspace-shell/`, and that no code comment describes the extension toolbar as a workspace control. The spec, plan, research D6, contracts and data model were reconciled on 2026-09-12; this is the check that implementation did not quietly re-collapse the distinction, which is the mistake an earlier draft of D6 made
- [X] T055 Run `npx tsc -b --noEmit` from `src/AskLucy.Web/ClientApp` — note the `-b`; a bare `tsc --noEmit` checks nothing in this repo
- [X] T056 Run `npm run lint` and `npm test` (the **full** frontend suite, not only touched files — `ChatPage.test.tsx` asserts viewer and panel behaviour independently of the components' own test files). Confirm `viewer/engine/ViewerEngine.contract.test.ts` passes unchanged: it is what actually evidences FR-012, since this feature adds no test of its own for "no viewer command changed meaning" and instead relies on specs/027's existing contract suite still holding
- [X] T057 Walk every scenario in `specs/050-viewer-extension-framework/quickstart.md`, especially Scenario 1's manual pass over the four migrated capabilities — the automated suites are the primary evidence, but drag, resize and real pointer interaction are what jsdom cannot substitute for

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS every user story
- **US1 (Phase 3)**: Depends on Foundational. Carries all regression risk
- **US2 (Phase 4)**: Depends on Foundational; T029 also depends on specs/049's panel registry, already shipped
- **US3 (Phase 5)**: Depends on Foundational. Independent of US1 and US2 in principle, but only meaningful once something can fail
- **US4 (Phase 6)**: Depends on Foundational only — genuinely independent
- **Polish (Phase 7)**: Depends on all desired stories

### Within User Story 1 — strictly sequential

The migration order is prescribed, not incidental: **panels → POI marker → confidence badge → site boundary overlay**. Each migration is followed by running that capability's existing suite before the next begins, so a regression is attributable to one capability rather than four.

### Parallel Opportunities

- T006, T008, T010, T012, T014 — the foundational unit tests, each against a different module
- T020 and T021 — the POI and badge extensions are independent modules
- T030, T032, T036, T038 — US2's tests
- T044, T045, T046 — US3's tests
- **US4 can run fully in parallel with US1, US2 and US3** — it touches only the loader's second state axis
- **US1's migrations cannot be parallelised with each other**, by design (see above)

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1: Setup
2. Phase 2: Foundational — **critical**, blocks everything
3. Phase 3: User Story 1
4. **STOP and VALIDATE**: quickstart Scenarios 1 and 2 — nothing changed for the user, and the viewer names no capability
5. At this point the feature's entire structural claim is already true

### Incremental Delivery

1. Setup + Foundational → the framework exists, nothing uses it
2. US1 → four capabilities migrated (**MVP** — SC-001 and SC-002 demonstrable)
3. US2 → a new capability costs one module and one line (SC-003)
4. US3 → every failure visible and contained (SC-005, SC-006)
5. US4 → the toggleable axis
6. Polish → spec reconciled, full regression pass

### Risk Notes

- **T018 is the first irreversible step.** Once `ViewerSurface` stops mounting `FloatingPanelHost` directly, panels only work through the framework. T019 is the gate that proves it before three more capabilities follow.
- **T024 is the highest-risk single task.** The site boundary overlay carries the most post-release history in this area. Migrate it last, change only its registration, and run specs/042 immediately after.
- **T029 changes specs/049 code.** Withdrawing a live panel kind while a panel of it is open touches `floatingPanelStore`, which specs/049 just stabilised — run the full panel suite, not just the touched file.
- **T036 adds a new surface to `ViewerSurface`.** The viewer area already carries the weather widget and boundary confidence badge top-left and the panel-hub indicator bottom-left; a colliding toolbar is a visual regression that no automated test will catch.
- **T051c needs its "before" measurement taken first.** Once the migration lands there is no baseline left to compare against, so capture it before T018.

---

## Notes

- [P] = different files, no dependencies
- Commit after each task or logical group
- Run the full frontend suite, never just the touched files
- The existing specs/028, specs/038 and specs/042 suites are the regression guard — they must pass unchanged in substance (FR-035)
