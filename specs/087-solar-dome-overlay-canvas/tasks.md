# Tasks: Solar Dome on Its Own Overlay Surface

**Input**: Design documents from `/specs/087-solar-dome-overlay-canvas/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/overlay-surface.md](./contracts/overlay-surface.md), [quickstart.md](./quickstart.md)

**Tests**: Required by the spec (FR-017, FR-018, FR-019) — the three test files below are deliverables, not optional.

**Organization**: Tasks are grouped by user story. Client-only change: every path is relative to the repository root; run commands from `src/AskLucy.Web/ClientApp`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to (US1, US2, US3)
- Every task names the exact file path it changes

## Phase 1: Setup

**Purpose**: Establish the pre-change baseline so a regression is attributable.

- [X] T001 Run `npx vitest run src/viewer/scene src/viewer/layers/gis src/features/solar` from `src/AskLucy.Web/ClientApp` and record the passing baseline (the solar suite is the regression net for FR-016).
- [X] T002 Re-read `specs/087-solar-dome-overlay-canvas/contracts/overlay-surface.md` and confirm the three seams against `src/AskLucy.Web/ClientApp/src/viewer/scene/DrawingSpaceRegistry.ts`, `src/viewer/layers/gis/GoogleMapsGisLayer.ts` and `src/features/solar/scene/SolarScene.ts` before writing code.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The three jsdom-testable seams every story is built on. No bridge or capability change happens here.

**⚠️ CRITICAL**: No user story work can begin until these compile and their tests pass.

- [X] T003 [P] Create `src/viewer/scene/overlayCamera.ts` exporting `applyMapTransformToCamera(camera: THREE.Camera, matrix: ArrayLike<number>): void` — copy the matrix into `projectionMatrix` and its inverse into `projectionMatrixInverse`, reset position/quaternion/scale to identity, then `updateMatrixWorld(true)` so `matrixWorld`/`matrixWorldInverse` are identity (research D3).
- [X] T004 [P] Write `src/viewer/scene/overlayCamera.test.ts` against a real `THREE.PerspectiveCamera`: projection matrix equals the input; `projectionMatrixInverse × projectionMatrix ≈ I`; a camera pre-moved/rotated ends with identity `matrixWorld` and `matrixWorldInverse`.
- [X] T005 [P] Create `src/viewer/layers/gis/overlaySurface.ts` per the contract: `createOverlaySurface(container, createRenderer?)` appends a `<canvas>` (`position:absolute; inset:0; pointer-events:none`, z-index above the map canvas) to the container, builds `new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true })`, sets pixel ratio 1, transparent clear colour, sRGB output + ACES tone mapping, and returns `{ canvas, renderer, resize, dispose }` (research D1, D4, D5, D6).
- [X] T006 [P] Write `src/viewer/layers/gis/overlaySurface.test.ts` with an injected fake renderer factory (jsdom has no WebGL): canvas is appended with the required styles; `resize` calls `setSize(w, h, false)` and no-ops on an unchanged size; `dispose` calls `forceContextLoss()` then `dispose()`, removes the canvas, and is idempotent; a throwing factory propagates (the bridge is what logs).
- [X] T007 Add `readonly overlayGroup: THREE.Group` to `DrawingSpaceHandle` and `bindOverlay(scene: THREE.Scene | null)` to `src/viewer/scene/DrawingSpaceRegistry.ts` — every handle creates its own overlay group; `bindOverlay` parents every acquired one (replaying groups acquired before the scene existed) and unparents them on `null`; `release()` removes and disposes both groups (research D2).
- [X] T008 Extend `src/viewer/scene/DrawingSpaceRegistry.test.ts`: overlay groups are distinct per extension and never the interleaved group; `bindOverlay` replays previously acquired groups in acquisition order; `bindOverlay(null)` unparents without disposing; `release` removes the overlay group from its scene and disposes its geometry; the T036 isolation test's `Object.keys(handle)` assertion grows by exactly `overlayGroup` (no renderer, camera or scene added).

**Checkpoint**: `npx vitest run src/viewer/scene src/viewer/layers/gis` green; nothing in the app behaves differently yet.

---

## Phase 3: User Story 1 - The Dome Is Never Hidden Behind a Building (Priority: P1) 🎯 MVP

**Goal**: The whole dome renders on the transparent surface above the map, so no building can paint over it.

**Independent Test**: open the analysis over a site with tall neighbours and sweep heading 0–360° at both tilt limits; no part of the dome is ever covered by a building (quickstart Check 1).

### Implementation for User Story 1

- [X] T009 [US1] In `src/viewer/layers/gis/GoogleMapsGisLayer.ts`'s `overlay.onContextRestored`, dispose any surface left by a previous restore, then create the surface via `createOverlaySurface(options.container)`, create `overlayScene` (with one `AmbientLight(0xffffff, 1)`) and `overlayCamera`, and call `drawingSpaceRegistry.bindOverlay(overlayScene)`. Wrap creation in `try/catch` logging `[GoogleMapsGisLayer] Could not create the solar dome overlay surface:` and leaving the interleaved path untouched on failure (FR-006, FR-008, research D5).
- [X] T010 [US1] In the same file's `overlay.onDraw`, after the existing interleaved camera transform, call `applyMapTransformToCamera` for `camera` and for `overlayCamera` (one matrix), then render `overlayScene` inside its **own** `try/catch` logging `[GoogleMapsGisLayer] Solar dome overlay render failed:` — a failure there must not skip the interleaved render and vice versa (FR-001, FR-005, FR-007, FR-008, research D8).
- [X] T011 [US1] In `src/features/solar/scene/SolarScene.ts`, parent `sunPathFixedGroup` and `sunPathDatedGroup` to `drawingSpace.overlayGroup` instead of `drawingSpace.group`; keep `buildingsGroup`/`groundGroup`/`lightGroup` on `drawingSpace.group`; apply `setGroundOffset` to both groups; delete the group `renderOrder` 10/11 assignments and their comment; add `setVisible(visible: boolean)` that sets both parent groups (FR-002, FR-003, FR-010, research D2).
- [X] T012 [US1] In `src/features/solar/scene/sunPathCurve.ts`, delete `buildDomeOccluder` and the `occluder` field of `FixedFurnitureObjects`; in `buildTubeFromPoints` use `depthTest: true` and `depthWrite: opacity >= 1`; in `buildDomeShell` use `depthTest: true` and keep `depthWrite: false`; in `buildDatedPath`'s current-position marker use `depthTest: true, depthWrite: true`; keep the hour-mark sprite's `depthTest: false`; keep `shell.renderOrder = 1` with a comment that it is intra-dome ordering (FR-011, FR-012, research D7).
- [X] T013 [US1] In `src/features/solar/scene/sunPathCurve.ts`'s `buildFixedFurniture`, drop `occluder: buildDomeOccluder(radiusMetres)` from the returned object and update the surrounding doc comments that describe the shared-depth workaround.
- [X] T014 [US1] Update `src/features/solar/scene/sunPathCurve.test.ts`: delete the occluder suite and the "dome draws last, over the massing" suite; invert the depth assertions to `depthTest: true` with `depthWrite` following opacity; add the current-position marker's depth flags; give `fakeDrawingSpace()` an `overlayGroup` (FR-011, FR-012, SC-004).
- [X] T015 [US1] Write `src/features/solar/scene/SolarScene.overlay.test.ts`: after construction and after a full `updateSunPath` + `rebuildBuildings`, `sunPathFixedGroup`/`sunPathDatedGroup` are children of `overlayGroup` and absent from `group`; `buildingsGroup`/`groundGroup`/`lightGroup` are the reverse; `setGroundOffset` and `setVisible` affect both parent groups (FR-002, FR-003, FR-014, FR-018).
- [X] T016 [US1] In `src/features/solar/extensions/builtin/solarAnalysisExtension.tsx`, replace the three direct `drawingSpace.group.visible` writes with `sceneRef.current.setVisible(...)` so closing/reopening the analysis hides and shows the overlay content too (FR-014).
- [ ] T017 [US1] Run quickstart Check 1 (occlusion sweep at both tilt limits) and Check 5's `rg` sweep over `src/AskLucy.Web/ClientApp/src/features/solar`, recording the result in the task.

**Checkpoint**: the dome is visible over every building, and the analysis still opens, closes and scrubs.

---

## Phase 4: User Story 2 - The Dome Stays Registered to the Map (Priority: P2)

**Goal**: The overlay matches the map's size, pixel ratio and camera on every frame, so the dome never slides or stretches against the map.

**Independent Test**: note the dial's "N" against a fixed building corner; rotate 90°, zoom two steps, resize the window — the "N" returns to the same feature with no stretch (quickstart Check 3).

### Implementation for User Story 2

- [X] T018 [US2] In `src/viewer/layers/gis/GoogleMapsGisLayer.ts`'s `onDraw`, extend the existing `lastCanvasWidth`/`lastCanvasHeight` branch to also call `overlaySurface.resize(gl.canvas.width, gl.canvas.height)`, so the overlay's drawing buffer tracks the map canvas (FR-004, research D4).
- [X] T019 [US2] In the same file, seed the overlay's size in `onContextRestored` right after creation, beside the existing `renderer.setSize(...)`, so the first frame is not drawn into an unsized buffer (FR-004).
- [X] T020 [US2] Confirm the overlay surface never intercepts input and that map gestures still work with the analysis open — quickstart Check 4, plus a DOM assertion in `overlaySurface.test.ts` that `pointer-events: none` and `inset: 0` are set (FR-004).
- [X] T021 [US2] Confirm the theme re-tint of the hour lines still runs unchanged against the overlay (existing test in `src/features/solar/scene/sunPathCurve.test.ts`) and that the dial/shell brightness matches the previous render in both themes — quickstart Check 8 (FR-013).
- [ ] T022 [US2] Run quickstart Check 3 (registration across heading, zoom and window resize) against `src/viewer/engine/MapRenderTarget.tsx`'s mounted map, and Check 7's navigation half (leave the workspace and return; the dome is in the same place with the camera preserved) (FR-010, FR-015).

**Checkpoint**: the dome is pixel-registered to the map across every camera and layout change.

---

## Phase 5: User Story 3 - No Workarounds, No Silent Failures, Nothing Left Behind (Priority: P3)

**Goal**: The surface's lifecycle is complete — disposed on context loss, removal and teardown — and every failure reaches the console.

**Independent Test**: force the second context to fail (one logged error, map still works); open/close the analysis five times and toggle the theme twice (exactly one overlay canvas while open, none after; no WebGL-context warnings).

### Implementation for User Story 3

- [X] T023 [US3] In `src/viewer/layers/gis/GoogleMapsGisLayer.ts`, route `overlay.onContextLost`, `overlay.onRemove` and the handle's `dispose()` through one teardown that calls `overlaySurface.dispose()` and `drawingSpaceRegistry.bindOverlay(null)`, then clears the local references so a second call is a no-op (FR-009, research D6).
- [X] T024 [US3] In the same file, ensure `handle.dispose()` also disposes the overlay surface before `renderer?.dispose()` and that nothing else references the overlay scene afterwards (FR-009).
- [ ] T025 [US3] Run quickstart Check 6 (forced creation failure: exactly one logged error from `src/viewer/layers/gis/GoogleMapsGisLayer.ts`, map and boundary still work) and Check 7 (five open/close cycles, two theme toggles, canvas count exactly one/none, no context warnings) (FR-008, FR-009, SC-005, SC-006).
- [X] T026 [US3] Run quickstart Check 5's code search and confirm the only remaining `depthTest: false` in `src/features/solar/scene/sunPathCurve.ts` is the hour-mark sprite's, and that no `buildDomeOccluder` or group `renderOrder` reference survives (SC-004).

**Checkpoint**: all three stories pass independently; nothing accumulates across cycles.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T027 Update `src/features/solar/README.md`: the module map gains `viewer/scene/overlayCamera.ts` and `viewer/layers/gis/overlaySurface.ts`, and the dome's own section states that the dome draws on the viewer-owned overlay surface above the map, why the interleaved pass could not work, and what must stay true (dome groups only, no renderer in a capability, disposal scope).
- [X] T028 Run the full gate from `src/AskLucy.Web/ClientApp`: `npx tsc -b --noEmit`, `npm run lint`, `npx vitest run` — all green, including the three new test files and the three extended ones (quickstart Check 9).
- [X] T029 Re-read `specs/087-solar-dome-overlay-canvas/spec.md`'s FR list against the diff and record, in the PR/commit message, which FR each change satisfies; confirm FR-016 (no solar math, shadow, panel, figures, day-length, boundary or backend change) holds by inspecting the diff's file list.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; blocks all stories.
- **US1 (Phase 3)**: depends on Phase 2. Delivers the MVP.
- **US2 (Phase 4)**: depends on US1's bridge wiring (T009–T010) — there is nothing to size or register until the surface exists.
- **US3 (Phase 5)**: depends on US1's bridge wiring; independent of US2's resize work.
- **Polish (Phase 6)**: depends on all desired stories.

### User Story Dependencies

- **US1**: no dependency on US2/US3 — the dome is visible over buildings with the surface wired and the dome moved.
- **US2**: benefits from US1 but its checks (registration, resize, theme, navigation) are separable.
- **US3**: benefits from US1; its lifecycle/failure checks do not touch US2's resize path.

### Within Each Story

- T003/T005/T007 (new modules and registry) precede every bridge task; tests T004/T006/T008 are written alongside their module.
- T009 → T010 (same file, ordered).
- T011 → T016 (the extension calls `setVisible`, which T011 adds).
- T012 → T013 (the builder returns what its caller reads), T014 after both.
- T023 → T024 (same file, ordered).

### Parallel Opportunities

- **Phase 2**: T003–T006 run in parallel (two new modules, two new test files); T007 then T008.
- **Phase 3**: T011 and T012 touch different files and can run in parallel; T014/T015 (two test files) in parallel afterwards; T016 after T011.
- **Phase 5**: T023/T024 are one file and sequential; T025/T026 are validation and can run in parallel.

---

## Parallel Example: Phase 2

```text
T003 Create src/viewer/scene/overlayCamera.ts
T005 Create src/viewer/layers/gis/overlaySurface.ts
T004 Write src/viewer/scene/overlayCamera.test.ts   (after T003)
T006 Write src/viewer/layers/gis/overlaySurface.test.ts (after T005)
```

## Parallel Example: Phase 3

```text
T011 Move the dome groups to drawingSpace.overlayGroup in src/features/solar/scene/SolarScene.ts
T012 Delete the occluder and depth opt-outs in src/features/solar/scene/sunPathCurve.ts
T014 Update src/features/solar/scene/sunPathCurve.test.ts        (after T012/T013)
T015 Write src/features/solar/scene/SolarScene.overlay.test.ts   (after T011)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 baseline, Phase 2 seams.
2. Phase 3 — the dome draws over buildings.
3. **STOP and VALIDATE** quickstart Check 1 on a live vector map. Deploy/demo if ready.

### Incremental Delivery

1. Seams → US1 (occlusion fixed) → validate → ship.
2. US2 (registration holds across resize/zoom/theme/navigation) → validate → ship.
3. US3 (lifecycle, failure surfacing, cleanup) → validate → ship.

---

## Notes

- [P] = different files, no ordering dependency.
- No backend, API, database or panel-content change is permitted (FR-016).
- The three new test files are deliverables of FR-017/018/019, not optional coverage.
- Commit after each phase; run the affected test subset before moving on.
