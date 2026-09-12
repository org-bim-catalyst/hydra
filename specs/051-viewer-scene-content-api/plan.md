# Implementation Plan: Viewer Scene and Content API

**Branch**: `051-viewer-scene-content-api` | **Date**: 2026-09-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/051-viewer-scene-content-api/spec.md`

## Summary

Grows the viewer's published command surface from a map-shaped set (`addLayer`/`zoomToLocation`/
`setMapStyle`) into one that owns georeferenced 3D content end to end: content Lucy can load,
replace and clear; a single reference point and published coordinate conversion every capability
positions against; isolated drawing spaces extensions render into; viewer-owned redraw scheduling;
published camera state; and addressable, selectable elements with readable properties. It is
additive to the existing `IViewerEngine` surface (specs/027) and extends specs/050's extension
context with drawing/content/element capabilities — nothing published today changes meaning.

The technical approach: a new `ViewerContent` abstraction wraps the existing `RenderLayer`
bookkeeping with richer lifecycle (loading/loaded/failed), format validation and world placement;
a single `SceneAnchor` owned by the engine replaces the ad hoc re-anchoring logic currently living
inside `GoogleMapsGisLayer.ts`, publishing one ENU (East-North-Up) coordinate conversion built on
the same `transformer.fromLatLngAltitude` call the map's own camera already uses every frame; each
drawing capability receives its own `THREE.Group` ("Drawing Space") added to the one scene the map
bridge already owns, never the scene or renderer directly; `engine.invalidate()` becomes the only
sanctioned redraw request, coalesced into the existing `WebGLOverlayView.requestRedraw()` call
that today fires unconditionally every frame (a real bug this feature fixes); and the renderer's
long-undeclared color/lighting treatment is set once, reviewed against every existing visual
capability, here.

## Technical Context

**Language/Version**: TypeScript 5.x (frontend), strict mode — matches the existing viewer package.

**Primary Dependencies**: `three` ^0.185.1 (already a dependency — `THREE.Group`, `GLTFLoader` from
`three/examples/jsm/loaders/GLTFLoader.js`, `THREE.WebGLRenderer` color-management APIs),
`@googlemaps/js-api-loader` (existing — `WebGLOverlayView`/`MapsLibrary` bridge unchanged),
`zustand` (existing store convention), `zod` (existing action-allowlist validation convention).

**Storage**: N/A — session-scoped client state only, matching every existing viewer store
(`viewerEngineStore`, `googleMapsStore`, specs/050's `viewerExtensionStore`). Content sources are
read through the platform's existing signed-URL file access mechanism (spec Assumptions); no new
persistence.

**Testing**: Vitest + Testing Library + jest-axe (existing frontend convention). `three` objects
(geometry/material disposal, group parenting) are asserted directly against the `THREE.Scene`
graph in jsdom — no WebGL context needed for structural assertions, matching how
`SiteBoundaryRenderer`'s existing tests already work.

**Target Platform**: Browser (Chromium/Firefox/Safari, desktop and mobile viewports) — same as the
existing viewer.

**Project Type**: Web application, frontend-only feature (`src/AskLucy.Web/ClientApp`). No backend
change beyond a new capability class if Lucy's content-loading capability needs its own MediatR
command (evaluated in research.md D8) — the existing capability-dispatch mechanism is reused, not
replaced.

**Performance Goals**: SC-004/SC-006 — no measurable frame-rate degradation with several drawing
capabilities active versus one, and zero redraws while idle. No new numeric budget beyond
"no regression against the pre-existing single-capability baseline," since this environment cannot
run a GPU benchmark (same limitation documented for specs/050's SC-007).

**Constraints**: Single WebGL context per `WebGLOverlayView` bridge (unchanged — this feature adds
no second renderer). Global renderer state (shadow maps, tone mapping, color space) is inherently
scene-wide in Three.js — this is exactly the constraint FR-016/FR-017/FR-018 exist to govern, not
route around.

**Scale/Scope**: One 3D content format (glTF — research.md D1) for the initial content-loading
surface; "modest amounts of content" per capability (spec Assumptions) — no LOD/streaming.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Clean Architecture & Dependency Rule | Frontend-only feature; no backend layer change unless D8 needs a capability class, which would follow the exact shape specs/049's `PresentPanelContentCapability` already established (Application → Domain only, Infrastructure/Api unchanged pattern). PASS. |
| II. SOLID | The engine gains additive methods, not a redesigned interface — existing `IViewerEngine` consumers are unaffected (OCP). `SceneAnchor`, `RedrawScheduler`, and `DrawingSpaceRegistry` are each a single-reason-to-change module (SRP), composed into `ViewerEngine` rather than inherited. PASS. |
| III. DRY/KISS/YAGNI | The ENU conversion replaces two independent ad hoc implementations (`GoogleMapsGisLayer.ts`'s `toLocalMeters` and the backend's `GeometryMath.ToLocalMeters`, per research D2) with one published client-side source — direct DRY win, not a premature abstraction, since the duplication is real and already causing drift risk. One 3D format (glTF) rather than several, per spec Assumptions (YAGNI). PASS. |
| IV. Composition over Inheritance | Drawing spaces are composed `THREE.Group` instances handed out by the engine, not a `DrawableExtension` base class — an extension's `start()` still returns nothing (specs/050's factory-function shape, no inheritance introduced). PASS. |
| V. Dependency Inversion & Testability | `IViewerEngine`'s new methods remain interface-first, implemented by the concrete `ViewerEngine` class exactly as today; scene-graph assertions in tests operate on plain `THREE.Object3D` structures without a live WebGL context. PASS. |
| VI. Separation of Concerns | Drawing/positioning/redraw-scheduling logic stays inside `viewer/`; `ViewerSurface.tsx` continues to reference only the engine, never a capability's internals (specs/050's SC-002 standard, extended here to content — see research D1). PASS. |
| VII. Convention over Configuration | Follows the existing `viewer/api/*.ts` + `viewer/engine/ViewerEngine.ts` + Zustand-store convention exactly; no new state-management mechanism introduced. PASS. |
| VIII. No Silent Failures | Every content/drawing failure (FR-035–037) is required to reach a visible outcome and be recorded — this is the constitution's own principle stated as a functional requirement, reusing specs/050's `ExtensionFailureNotice` pattern rather than inventing a second one (research D9). PASS. |
| §7 UI / Accessibility | No new interactive UI surface beyond what specs/049/050 already cover (panel content, toolbar, failure notice); loading/failure indication reuses those existing, already-accessible patterns. PASS. |
| §8 Security | Content sources are constrained to the platform's existing signed-URL/entitlement mechanism (spec Assumptions) — no arbitrary external fetch is introduced, closing the SSRF-shaped risk the spec explicitly calls out. PASS. |
| §10 Testing | Unit tests for `SceneAnchor`'s coordinate math, `RedrawScheduler`'s coalescing, `DrawingSpaceRegistry`'s isolation and disposal, and the extended action-allowlist entries — all runnable without a real WebGL context, matching existing viewer test conventions. PASS. |

**Gate result: PASS.** No Complexity Tracking entries required — every design choice below either
extends an existing, established pattern (Zustand store, factory-composed engine module,
zod-validated allowlist entry) or removes existing duplication (the ENU conversion). See research.md
for the specific decisions and alternatives considered.

**Re-evaluated post-Phase-1 design (2026-09-12): still PASS.** Nothing in data-model.md or
contracts/ introduced a new backend datastore, a new dispatch mechanism, or an advisory (rather
than structural) isolation guarantee — Lucy loads content through specs/049's existing capability
pattern (D8), and Drawing Space isolation is enforced by never handing out the underlying
`THREE.Scene`/`Camera`/`Renderer` at all, not by a convention a capability could violate.

## Project Structure

### Documentation (this feature)

```text
specs/051-viewer-scene-content-api/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/viewer/
├── api/
│   ├── commands.ts          # extended: ViewerCommand union gains content/drawing/camera/element entries
│   ├── engine.ts            # extended: IViewerEngine gains additive methods
│   ├── events.ts            # extended: ViewerEvent union gains contentLoading/contentFailed/cameraChanged
│   └── coordinateFrame.ts   # NEW: ENU convention + WorldPlacement types, published conversion contract
├── content/                 # NEW — Viewer Content lifecycle
│   ├── ViewerContent.ts     # entity shape, ContentSource discriminated union
│   ├── contentStore.ts      # Zustand store: content instances, load state
│   └── loaders/
│       └── gltfContentLoader.ts   # the one supported 3D format (research D1)
├── scene/                   # NEW — the single Three.js scene the map bridge owns, exposed
│   ├── SceneAnchor.ts       # the one reference point + fromLatLngAltitude-based conversion
│   ├── DrawingSpaceRegistry.ts   # per-capability THREE.Group issuance + disposal tracking
│   ├── RedrawScheduler.ts   # invalidate() coalescing, per-frame subscriptions
│   └── rendererState.ts     # declared drawing requirements + the one-time color/lighting treatment
├── elements/                 # NEW — Element addressing, properties, selection framing
│   └── elementIndex.ts
├── engine/
│   └── ViewerEngine.ts      # extended: composes the above, additive methods only
├── layers/gis/
│   └── GoogleMapsGisLayer.ts   # modified: onDraw uses RedrawScheduler instead of unconditional
│                                # requestRedraw(); onContextRestored applies rendererState.ts;
│                                # sceneAnchor re-anchoring logic moves to scene/SceneAnchor.ts
├── extensions/
│   ├── context.ts            # extended: contributeDrawing/onFrame/loadContent/getCameraState etc.
│   └── ViewerExtension.ts    # extended: Contribution union gains a 'drawingSpace' kind
├── panels/actions/
│   └── allowlist.ts          # extended: the commands FR-032 names as safe for content to invoke
└── features/viewer/components/
    └── ViewerSurface.tsx     # modified: startup map load routes through loadContent (FR-003)
```

**Structure Decision**: Frontend-only, within the existing `viewer/` package. Three new top-level
modules (`content/`, `scene/`, `elements/`) rather than folding this into `engine/` — each owns a
genuinely separate concern (what is shown vs. where it sits in the world vs. what is addressable
within it) and each is independently unit-testable without the others, matching Principle II (SRP)
and the existing `panels/`/`extensions/` sibling-module convention. No backend project is added;
D8 in research.md settles whether Lucy's content-loading capability needs a new backend command or
can reuse an existing dispatch path.

## Complexity Tracking

*No entries — see Constitution Check above.*
