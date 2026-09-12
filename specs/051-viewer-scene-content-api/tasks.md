# Tasks: Viewer Scene and Content API

**Input**: Design documents from `/specs/051-viewer-scene-content-api/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — this feature fixes two real existing bugs (unconditional redraw, ad hoc
per-file reference-point re-anchoring) and specs/050's own experience shows structural guarantees
here need asserting, not assuming (research D3/D4).

**Organization**: Tasks are grouped by user story (spec.md priorities P1/P1/P2/P2/P3).

## Path Conventions

All paths relative to `src/AskLucy.Web/ClientApp/src/viewer/` unless stated otherwise (frontend-only
feature; a backend capability class lives under `src/AskLucy.Application/` — see T033).

---

## Phase 1: Setup

- [X] T001 Create directories `content/`, `content/loaders/`, `scene/`, `elements/` under `viewer/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The single reference point, the published coordinate conversion, redraw scheduling,
and drawing-space isolation — everything every user story below is built on. **⚠️ This phase
carries this feature's own regression risk**: it modifies `GoogleMapsGisLayer.ts`, the one file
specs/050's four migrated capabilities all ultimately depend on.

- [X] T002 [P] Define `ContentSource`, `WorldPlacement`, `ReferencePoint`, `LocalPosition`, `CameraState`, `ElementProperties`, `ContentLoadState`, `ContentFailureReason` types per data-model.md in `content/ViewerContent.ts`, `scene/SceneAnchor.ts` (co-located type exports), and `elements/elementIndex.ts`
- [X] T003 Implement `api/coordinateFrame.ts` — `worldToLocal`/`localToWorld`, built on the same `transformer.fromLatLngAltitude` matrix math `GoogleMapsGisLayer.onDraw` already uses (research D2). ENU convention (X=East, Y=North, Z=Up) stated once in this file's doc comment
- [X] T004 [P] Unit tests for `coordinateFrame.ts` in `api/coordinateFrame.test.ts` — round-trip `worldToLocal`/`localToWorld` within stated tolerance; a change in reference point produces correct (non-stale) results for both old and new positions (quickstart Scenario 3)
- [X] T005 Implement `scene/SceneAnchor.ts` — owns the one `ReferencePoint` (FR-008); `set()`/`get()`; no capability may call `set()` except the engine itself on first content load
- [X] T006 [P] Unit tests for `SceneAnchor.ts` in `scene/SceneAnchor.test.ts` — single reference point enforced; `get()` before any content loads returns `null`
- [X] T007 Implement `scene/RedrawScheduler.ts` — `invalidate()` coalescing multiple calls within one frame into a single `requestRedraw` callback (FR-020, FR-021, FR-022, research D4)
- [X] T008 [P] Unit tests for `RedrawScheduler.ts` in `scene/RedrawScheduler.test.ts` — N calls before the next frame produce exactly one downstream redraw call; zero calls produce zero; a call after the scheduler is told a subscriber stopped is a safe no-op (FR-024)
- [X] T009 Implement `scene/DrawingSpaceRegistry.ts` — constructor takes a `THREE.Scene` via injection (the real map-bridge scene isn't wired in until T037; T010 tests against a plain `new THREE.Scene()`); `acquire(extensionId)` returns a `DrawingSpaceHandle` (`group`, `invalidate`, `onFrame`, `declareDrawingRequirement`), groups appended to the scene in acquisition order (FR-015); `release(extensionId)` removes the group from the scene and disposes every descendant's geometry/material/texture (FR-014, FR-033, research D3)
- [X] T009a Implement `onFrame` callback containment in `DrawingSpaceRegistry.ts` — each subscriber's callback runs inside a try/catch; a thrown callback is recorded (emits a new `drawingCallbackFailed` event, T018) and MUST NOT stop other subscribers' callbacks or the render loop (FR-019, constitution §2.VIII NON-NEGOTIABLE — mirrors specs/050's T033 event-handler containment for the new drawing/frame-callback surface)
- [X] T010 [P] Unit tests for `DrawingSpaceRegistry.ts` in `scene/DrawingSpaceRegistry.test.ts` — two acquired handles have distinct, non-overlapping groups; `release()` removes the group from the scene and clears its `onFrame` subscriptions; `acquire()` called twice for the same extension id returns the same handle (idempotent, mirrors specs/050 start-when-started posture)
- [X] T010a Unit test for T009a's containment in `scene/DrawingSpaceRegistry.test.ts` (same file as T010 — sequential, not parallel) — one drawing space's throwing `onFrame` callback does not prevent another's from running on the same frame, and the failure is surfaced via `drawingCallbackFailed` rather than swallowed (FR-019)
- [X] T010b Unit test for draw-order stability in `scene/DrawingSpaceRegistry.test.ts` (same file as T010/T010a — sequential) — groups are children of the scene in acquisition order; a release-then-re-acquire cycle for one extension does not reorder unrelated, still-active groups (FR-015)
- [X] T011 Implement `scene/rendererState.ts` — `declareRequirement(extensionId, requirement)`/`withdrawRequirements(extensionId)`, resolving the union of currently-declared requirements onto the renderer and reporting a conflict via a callback (FR-016, FR-017, research D3)
- [X] T012 [P] Unit tests for `rendererState.ts` in `scene/rendererState.test.ts` — a requirement declared by one extension and withdrawn by another has no effect; a requirement stays applied while at least one declarer remains; a stated conflict case invokes the report callback rather than silently picking a side
- [X] T013 Apply the one-time renderer color/lighting treatment in `GoogleMapsGisLayer.ts`'s `onContextRestored` — `renderer.outputColorSpace = THREE.SRGBColorSpace`, `renderer.toneMapping = THREE.ACESFilmicToneMapping` (FR-018, research D5). Record in `quickstart.md` Scenario 9 that the visual before/after comparison against `SiteBoundaryRenderer`'s colors needs a human with a live browser — do not claim it verified
- [X] T014 Remove the ad hoc `sceneAnchor` re-anchoring logic from `GoogleMapsGisLayer.ts` (the `setSiteBoundary`-triggered re-anchor to the boundary's own centroid) and replace every local-meters computation with `SceneAnchor`/`coordinateFrame.ts` calls (FR-008, research D2) — `SiteBoundaryRenderer`'s positioning must be unaffected
- [X] T015 Replace `GoogleMapsGisLayer.onDraw`'s unconditional `overlay.requestRedraw()` call with a `RedrawScheduler`-driven one — only redraws when something has actually called `invalidate()` (FR-020, FR-022, research D4)
- [X] T016 Run the full specs/042 site-boundary suite and specs/050's four migrated-capability suites; confirm they pass unchanged after T013–T015 — this is the checkpoint that proves the map-bridge changes didn't regress anything before any new story builds on top
- [X] T017 [P] Add `loadContent`, `replaceContent`, `unloadContent`, `listContent`, `getReferencePoint`, `getCameraState`, `getElementInfo`, `selectAndFrame`, `invalidate` to `api/engine.ts`'s `IViewerEngine` interface and `api/commands.ts`'s `ViewerCommand` union per contracts/viewer-engine-api-extensions.md — signatures only, additive (FR-038, FR-039)
- [X] T018 [P] Add `contentLoading`, `contentFailed`, `cameraChanged`, `drawingRequirementConflict`, `drawingCallbackFailed` to `api/events.ts`'s `ViewerEvent` union per contracts/viewer-engine-api-extensions.md (the last one added for FR-019/T009a's containment reporting)
- [X] T019 [P] Add the `drawingSpace` and `frameSubscription` kinds to specs/050's `Contribution` union in `extensions/ViewerExtension.ts`, and add `acquireDrawingSpace()` to `ExtensionContext` in `extensions/context.ts`, backed by `DrawingSpaceRegistry` (contracts/extension-context-extensions.md)
- [X] T020 [P] Extend specs/050's `withdrawContributions(id)` in `extensions/loader.ts` with two new branches — `drawingSpace` calls `release()`, `frameSubscription` calls `unsubscribe()` — mirroring the existing `livePanelKind`/`eventSubscription` branches exactly
- [X] T021 [P] Unit tests for T019/T020 in `extensions/context.test.ts` and `extensions/loader.test.ts` — `acquireDrawingSpace()` is idempotent per extension; stopping an extension releases its drawing space and clears its frame subscriptions, leaving other extensions' drawing spaces untouched (mirrors specs/050's T051b normal-isolation pattern)
- [X] T022 Wire `getCameraState`/`cameraChanged` — read camera values (`map.getCenter/getHeading/getTilt/getZoom`) through the existing `ViewerRenderTargetHandle` plumbing in `ViewerEngine.ts`; emit `cameraChanged` from the map's `'idle'` event in `MapRenderTarget.tsx` (FR-025, FR-026, research D6)
- [X] T023 [P] Unit tests for camera state in `engine/ViewerEngine.contract.test.ts` (extend, do not replace existing assertions) — `getCameraState()` returns the render target's current values; `cameraChanged` fires on idle, not per intermediate frame
- [X] T023a Implement `elements/elementIndex.ts`'s build/read functions — `buildElementIndex(nodes)` and `getElementProperties(index, elementId)`, keyed by `elementId`, reporting `{ hasProperties: false }` for an element with none (FR-031) — implemented in Foundational (not deferred to US3) so both US1's content loader (T027) and US3's engine methods (T044/T045) depend on one already-built module instead of US1 depending on US3's later work
- [X] T023b [P] Unit tests for `elementIndex.ts` in `elements/elementIndex.test.ts` — a node's `extras`/`userData` become readable properties; a node with none reports `hasProperties: false`, never an empty object (FR-031)

**Checkpoint**: Reference point, coordinate conversion, redraw scheduling, drawing-space isolation
(including failure containment and draw-order stability), and the element index all exist and are
tested; the existing map bridge is fixed and re-verified unchanged. No user story yet uses any of
it end to end.

---

## Phase 3: User Story 1 — Lucy Loads, Replaces and Clears Viewer Content (Priority: P1) 🎯 MVP

**Goal**: Content is asked for, not internally wired — the map still appears unasked at startup,
and Lucy can load/replace/clear what the viewer shows.

**Independent Test**: Ask Lucy to load content, then replace it, then clear it, confirming the
viewer reflects each request; confirm the map still appears on startup without anyone asking.

### Tests for User Story 1

- [X] T024 [P] [US1] Unit tests for `content/contentStore.ts` load-state transitions in `content/contentStore.test.ts` — `loading` → `loaded`/`failed`; an unsupported format resolves to `failed`/`unsupported-format` without throwing (data-model.md state diagram)
- [X] T025 [P] [US1] Unit tests for `loadContent`/`replaceContent`/`unloadContent`/`listContent` in `engine/ViewerEngine.test.ts` — replacing releases the previous content's `RenderLayer` and resources before the new content begins loading; unloading a non-existent id fails gracefully; listing reflects every currently-tracked content item independently removable (US1 AC2, AC3, AC6); a non-default `orientationDegrees`/`scale` in the placement is reflected in the loaded content's transform (FR-011)

### Implementation for User Story 1

- [X] T026 [US1] Implement `content/contentStore.ts` — Zustand store tracking `ViewerContent[]`, mirroring `viewerEngineStore`'s session-scoped, no-persist convention
- [X] T027 [US1] Implement `content/loaders/gltfContentLoader.ts` — loads a glTF via `GLTFLoader` (three/examples/jsm), reads node `extras`/`userData` and calls `elementIndex.ts`'s `buildElementIndex` (T023a) to populate it, reports `unsupported-format`/`unreachable-or-corrupt` distinctly (FR-007, FR-035)
- [X] T028 [US1] Implement `loadContent`/`replaceContent`/`unloadContent`/`listContent` on `ViewerEngine` — routes `{kind:'gis'}` sources through the existing `addLayer('gis', ...)` path unchanged, `{kind:'model'}` sources through `gltfContentLoader`; applies the placement's `orientationDegrees`/`scale` (not just position) to the loaded content's root transform (FR-011); emits `contentLoading` immediately and `contentLoaded`/`contentFailed` on resolution (FR-001, FR-002, FR-005, FR-006, research D1)
- [X] T029 [US1] Content with no placement resolves to `failed`/`unplaceable` rather than being placed arbitrarily (FR-013) — validated before any loader runs
- [X] T030 [US1] Route `ViewerSurface.tsx`'s startup effect through `engine.loadContent({ kind: 'gis', ... })` instead of `engine.addLayer(...)` directly (FR-003) — location-change updates keep using the existing `zoomToLocation`/`fitBounds` calls, unchanged
- [X] T031 [US1] Unit test for T030 in `features/viewer/components/ViewerSurface.test.tsx` — the map still appears automatically on startup with no request, asserting the existing WebGL-fallback/placeholder/map-transition assertions remain intact (quickstart Scenario 1)
- [X] T031a [P] [US1] Unit test confirming `floatingPanelStore`'s existing `contentLoaded` subscription (specs/049/specs/042 — marks a panel's context `stale`) behaves correctly when `contentLoaded` is fired via the new `loadContent`/`replaceContent` path, not only the pre-existing `displayContent` path — contracts/viewer-engine-api-extensions.md deliberately reuses this event rather than adding a new one
- [X] T032 [P] [US1] Add a loading indicator for `contentLoading` — extend `ExtensionFailureNotice`'s sibling surface or add a small Chip subscribed to `contentStore`'s loading state in `viewer/content/components/ContentLoadingIndicator.tsx`, mounted from `ViewerSurface.tsx` (FR-005)
- [X] T033 [US1] Backend: `LoadViewerContentCapability` in `src/AskLucy.Application/` — `IsAvailable`, `InputSchemaJson`, MediatR handler dispatching through the existing SSE/tool-result channel, mirroring specs/049's `PresentPanelContentCapability` shape exactly (FR-004, research D8)
- [X] T034 [P] [US1] Backend unit tests for `LoadViewerContentCapability` in `src/AskLucy.Application.Tests/` mirroring `PresentPanelContentCapabilityTests`' structure

**Checkpoint**: Lucy can load, replace and clear content end to end; the map still appears
automatically on startup. This is the MVP — SC-001 is demonstrable.

---

## Phase 4: User Story 2 — An Extension Draws Georeferenced Content (Priority: P1)

**Goal**: A capability draws into its own isolated space, positioned by real-world coordinates,
correctly placed through camera movement, fully cleaned up on stop.

**Independent Test**: An extension draws a known shape at known coordinates; confirm it appears
correctly positioned, stays correct through camera movement, and disappears completely on stop.

### Tests for User Story 2

- [X] T035 [P] [US2] Unit test in `extensions/extensibility.test.tsx` (extend specs/050's scratch-extension test) — a scratch extension calls `context.acquireDrawingSpace()`, adds a mesh positioned via `worldToLocal`, and the mesh appears in the scene at the expected local coordinates
- [X] T036 [P] [US2] Unit test for isolation in `scene/DrawingSpaceRegistry.test.ts` — extend T010 to (a) assert content added to one extension's group is structurally unreachable from another's handle (no shared object references), and (b) deliberately attempt, via every means `DrawingSpaceHandle`/`ExtensionContext` exposes, to reach or mutate another extension's group or the shared renderer/scene/camera directly, asserting there is no path that succeeds — satisfying SC-003's own stated verification method ("by inspection and by attempting it, with zero successful attempts") literally, not just structurally

### Implementation for User Story 2

- [X] T037 [US2] Connect `DrawingSpaceRegistry` (constructed with scene injection since T009) to the real, live `GoogleMapsGisLayer.ts` scene instance at runtime — this is runtime wiring, not first-time construction (T009 already accepts any `THREE.Scene`; T010's tests already exercise it against a plain one). Expose `acquireDrawingSpace()` (from T019) end to end through this live instance (internal wiring only; the scene itself is never exposed to a capability)
- [X] T038 [US2] Wire `declareDrawingRequirement` (from T011) end to end — a capability's call reaches `rendererState.ts` and is resolved onto the real `WebGLRenderer` instance `GoogleMapsGisLayer.ts` owns
- [X] T039 [US2] Emit `drawingRequirementConflict` (FR-017) from `rendererState.ts`'s report callback, routed through `ViewerEngine`'s event bus
- [X] T040 [US2] Content placed far from the current reference point remains correctly positioned after the reference point changes — integration test in `scene/SceneAnchor.test.ts` combining T003/T005 against a second, distant `loadContent` call (US2 AC5, FR-012)

**Checkpoint**: Extensions can draw georeferenced content, isolated from each other, correctly
positioned through camera and reference-point changes.

---

## Phase 5: User Story 3 — The User Inspects an Element (Priority: P2)

**Goal**: Selected elements have stable identity and readable properties; a panel reference can
select and frame an element in the viewer.

**Independent Test**: Load content with element information, select an element, confirm identity/
properties are readable; separately, activate an element reference in a panel and confirm the
viewer selects and frames it.

### Tests for User Story 3

- [X] T042 [P] [US3] Unit tests for `selectAndFrame` in `engine/ViewerEngine.test.ts` — selects and re-frames a known element; fails with a stated reason for an element that no longer exists (US3 AC4), never a silent no-op

### Implementation for User Story 3

- [X] T043 [US3] Confirm the element index built by T023a/T027 during content load is what `getElementInfo`/`selectAndFrame` (T044/T045) read from — an integration check, not a fresh implementation (the index module and its own tests already landed in Foundational, T023a/T023b, specifically so this story has no inverted dependency on work built here)
- [X] T044 [US3] Implement `getElementInfo(layerId, elementId)` on `ViewerEngine` (FR-027, FR-028)
- [X] T045 [US3] Implement `selectAndFrame(layerId, elementId)` on `ViewerEngine` — composes existing `select()` with a `zoomToLocation`/`fitBounds` framing call using the element's `WorldPlacement` (FR-029)
- [X] T046 [US3] Deterministic overlap resolution (FR-030) — hit-testing resolves by drawing-space insertion order (T009's acquisition order) then nearest-to-camera within one space; implemented alongside `select()`'s existing element-registration path
- [X] T047 [US3] Add `selectAndFrame` to `panels/actions/allowlist.ts` per contracts/action-allowlist-extension.md — `{layerId, elementId}` schema, mirrors the existing `select` entry (FR-032)
- [X] T048 [P] [US3] Unit test for T047 in `panels/actions/allowlist.test.ts` — `selectAndFrame` validates and invokes correctly; `loadContent`/`replaceContent`/`unloadContent` remain absent from the allowlist (contracts/action-allowlist-extension.md "Deliberately still excluded")

**Checkpoint**: Elements are addressable, selectable, and readable; a panel reference can drive
the viewer to select and frame one.

---

## Phase 6: User Story 4 — The Viewer Stays Responsive While Several Capabilities Draw (Priority: P2)

**Goal**: Multiple drawing capabilities coexist without degrading frame rate or leaking resources;
stopping one reclaims everything it used.

**Independent Test**: Run several drawing capabilities together and measure frame rate against a
single-capability baseline; repeatedly start/stop a drawing capability and confirm resource use
returns to baseline every time.

### Tests for User Story 4

- [X] T049 [P] [US4] Unit test for per-frame subscriptions in `scene/DrawingSpaceRegistry.test.ts` — `onFrame` callback fires once per actual redraw, not continuously when idle; ends automatically when the owning extension stops (FR-023, FR-024)
- [X] T050 [P] [US4] Fifty-cycle resource test in `scene/DrawingSpaceRegistry.test.ts` — acquire a drawing space, add geometry, declare a requirement, subscribe to frames, release; repeat fifty times; assert scene child count and requirement-declarer count return to baseline every cycle (SC-005, mirrors specs/050's T052 pattern that caught a real leak there)

### Implementation for User Story 4

- [X] T051 [US4] Migrate `SiteBoundaryRenderer`'s comet animation from its current direct `onDraw`-driven update to a `context.onFrame()` subscription via the panels/site-boundary extension (specs/050's `siteBoundaryExtension.tsx`) acquiring a Drawing Space — the first real consumer of per-frame subscriptions (research D4)
- [X] T052 [US4] Confirm replacing content releases the previous content's drawing resources in full — extend `content/contentStore.test.ts`/`ViewerEngine.test.ts` assertions from T025 with explicit dispose-call counting (FR-006, US4 AC5)
- [X] T053 [US4] Record the frame-rate comparison method in `quickstart.md` Scenario 5 as **not measured in this environment** (no GPU-capable browser here) — same honesty pattern as specs/050's SC-007/T057, not silently skipped

**Checkpoint**: Resource accounting is proven not to leak across repeated cycles; the one existing
continuously-animating capability now goes through the framework's own scheduling.

---

## Phase 7: User Story 5 — Content That Cannot Be Shown Explains Itself (Priority: P3)

**Goal**: Every content/drawing failure produces a distinct, understandable explanation while the
viewer keeps working.

**Independent Test**: Attempt to load unsupported, unreachable, corrupt, and unpositioned content;
confirm each produces a distinct, understandable explanation while the viewer remains usable.

### Tests for User Story 5

- [X] T054 [P] [US5] Unit tests for content failure surfacing in `viewer/extensions/components/ExtensionFailureNotice.test.tsx` (extend) — a `contentFailed` event with each `ContentFailureReason` produces a distinct message; a `drawingRequirementConflict` event produces its own distinct message
- [X] T055 [P] [US5] Unit test confirming already-displayed content remains displayed and the viewer stays usable after any content failure — extend `ViewerEngine.test.ts` (US5 AC4)

### Implementation for User Story 5

- [X] T056 [US5] Extend `ExtensionFailureNotice.tsx` to also read content/drawing failures (a `content` store subscription alongside its existing `viewerExtensionStore` one) and word each `ContentFailureReason`/`drawingRequirementConflict`/`drawingCallbackFailed` (T009a) distinctly (FR-019, FR-035, FR-037, research D9)
- [X] T057 [US5] Confirm every failure introduced by this feature reaches T056's notice by inspection across `content/`, `scene/`, and `elements/` — no `console.error`-only path (FR-037, constitution §2.VIII), mirroring specs/050's T047

**Checkpoint**: Every failure path in this feature's spec produces a visible, understandable outcome.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T058 [P] Update `viewer/README.md` with `content/`, `scene/`, and `elements/` sections covering the content lifecycle, the ENU coordinate convention, drawing spaces, and redraw scheduling
- [X] T059 Confirm the renderer color/lighting review (T013) and the frame-rate comparison (T053) are both recorded as open, human-required gaps in `quickstart.md` — not silently marked complete
- [X] T060 Run `npx tsc -b --noEmit` from `src/AskLucy.Web/ClientApp` — note the `-b`
- [X] T061 Run `npm run lint` and the **full** frontend suite (`npm test`) — confirm `viewer/engine/ViewerEngine.contract.test.ts` and every specs/050 migrated-capability suite pass unchanged (FR-038, FR-039, research D10)
- [X] T062 Walk every scenario in `quickstart.md`, especially Scenarios 1, 3, 7, and 8 — the automated suites are the primary evidence, but real pointer/camera interaction is what jsdom cannot substitute for

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS all user stories**. Carries this feature's
  own regression risk (modifies `GoogleMapsGisLayer.ts`); T016 is the gate before any story proceeds
- **US1 (Phase 3)**: Depends on Foundational only
- **US2 (Phase 4)**: Depends on Foundational only
- **US3 (Phase 5)**: Depends on Foundational only — the element index itself is built and tested in
  Foundational (T023a/T023b) specifically so US3 is not inverted-dependent on US1's later work; it
  is still practically most useful once US1's `gltfContentLoader` (T027) is populating the index
  with real content, but nothing in US3's own tasks requires US1's phase to have run first
- **US4 (Phase 6)**: Depends on Foundational and US2 (needs `acquireDrawingSpace` wired end to end
  by T037 before T051's migration can use it)
- **US5 (Phase 7)**: Depends on Foundational; benefits from US1/US2 existing so there are real
  failure paths to surface, but its own implementation (T056/T057) has no hard code dependency
- **Polish (Phase 8)**: Depends on all desired stories

### Parallel Opportunities

- T002–T023b in Foundational: most are `[P]` (different files) once their non-`[P]` prerequisite
  (e.g. T003 before T004, T005 before T006) lands — T009/T009a/T010/T010a/T010b are one sequential
  chain (same file, `scene/DrawingSpaceRegistry.ts`/`.test.ts`), and T013/T014/T015 are strictly
  sequential (same file, `GoogleMapsGisLayer.ts`); T016 is the checkpoint gate before Phase 3+
- US1 and US2 can proceed in parallel once Foundational completes (different files: `content/` vs
  `scene/`/`extensions/`)
- US3 can now also proceed in parallel with US1/US2 once Foundational completes (T023a/T023b
  removed its former dependency on US1's T027)
- US5 can proceed in parallel with US3/US4 once Foundational completes

### Risk Notes

- **T013–T015 are the highest-risk single tasks.** They modify the one file specs/050's four
  migrated capabilities all depend on. T016 is the gate that proves nothing broke before any new
  story builds on top — do not skip it or defer it to Polish.
- **T009a is a constitution-load-bearing task (§2.VIII NON-NEGOTIABLE).** Without it, an uncaught
  exception in any capability's `onFrame` callback has no defined containment path — do not treat
  it as optional polish.
- **T053/T059 need their comparisons taken by a human.** This environment cannot run a GPU
  benchmark or a visual color-pipeline comparison — document the gap, do not claim it closed.

---

## Notes

- [P] = different files, no dependencies
- Commit after each task or logical group
- Run the full frontend suite, never just touched files (specs/050's own lesson: `ChatPage.test.tsx`
  and the four migrated-capability suites assert viewer behavior independently of their own files)
- `ViewerEngine.contract.test.ts` is the load-bearing evidence for "no existing command changed
  meaning" (FR-038) — this feature adds no parallel test for that property
