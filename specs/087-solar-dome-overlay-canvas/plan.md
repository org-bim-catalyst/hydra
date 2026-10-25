# Implementation Plan: Solar Dome on Its Own Overlay Surface

**Branch**: `087-solar-dome-overlay-canvas` | **Date**: 2026-10-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/087-solar-dome-overlay-canvas/spec.md`

## Summary

Draw the whole sun-path dome in a second Three.js renderer on its own transparent DOM canvas, stacked above the map's canvas, so the map's 3D building pass — which runs after the interleaved `WebGLOverlayView.onDraw` and ignores the depth written there — can no longer paint over it. The map bridge (`GoogleMapsGisLayer`) owns the second surface, scene and camera, exactly as it owns the first; the solar extension receives a viewer-owned `overlayGroup` through its existing `DrawingSpaceHandle` and moves only its dome groups into it. Buildings, shadow ground and sun light stay interleaved. The three failed workarounds (near-plane depth mask, group `renderOrder` 10/11, `depthTest:false` on dome materials) are deleted.

## Technical Context

**Language/Version**: TypeScript 5.x (strict), React 19, Vite — client-only change.

**Primary Dependencies**: `three`, `@googlemaps/js-api-loader`, Google Maps JS `WebGLOverlayView`. No new dependency.

**Storage**: N/A (client rendering only; no backend, no schema, no API change).

**Testing**: Vitest + jsdom (unit), existing Playwright/manual quickstart for live-map verification (`GoogleMapsGisLayer.test.ts` documents that the real Maps/WebGL bridge is not jsdom-testable).

**Target Platform**: Browsers with WebGL2 and a **vector** Google Map (the overlay only exists there; raster/flat maps have no WebGL overlay today and are unchanged).

**Project Type**: Web application — frontend slice of the Ask Lucy viewer.

**Performance Goals**: no second animation loop; one overlay draw per map draw; no measurable frame-rate regression against the current interleaved dome (SC-003). The second renderer draws only the dome (~30 draw calls), so its cost is bounded and small.

**Constraints**: renderer-global settings stay in the viewer (specs/052 FR-038); capability reaches the surface only through `DrawingSpaceHandle` (specs/052 FR-037); no silent failures (constitution §2.VIII); workspace state survives navigation.

**Scale/Scope**: ~6 source files touched, 3 new (surface module + camera helper + tests), 2 new test files; ~10 FRs of new behaviour, ~5 tests updated.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Status |
|---|---|---|
| I. Clean Architecture / dependency rule | Frontend only; no backend layer crossings. | Pass |
| II. SOLID | The surface is created by its existing owner (the map bridge); the seam it exposes is one added field on an existing handle, no new abstraction layer. | Pass |
| III. Simplicity — DRY, KISS, YAGNI | One second renderer, one extra group, one pure function. No capability registry, no plugin surface, no config flag. Workarounds are **deleted** (net code reduction). | Pass |
| IV. Composition over inheritance | No inheritance introduced. | Pass |
| V. Dependency inversion & testability | Camera derivation is a pure function; surface creation takes an injectable renderer factory so lifecycle is testable without WebGL. | Pass |
| VI. Separation of concerns | Scheduling, surface and renderer remain viewer-owned; the solar feature keeps drawing only into its own group. | Pass |
| VII. Convention over configuration | Mirrors the existing `bind()`-after-construction convention used by `DrawingSpaceRegistry`, `RendererState`, `RedrawScheduler`, `activeScene`. No new config value. | Pass |
| VIII. No silent failures | Overlay creation failure and per-frame failures are logged through the same `console.error` path the interleaved pass uses, independently per surface; one surface failing never blocks the other. | Pass |
| TS strict / no `any` | All new code typed; no `any`. | Pass |
| Renderer-global settings in the viewer (specs/052 FR-038) | The second renderer is constructed, configured and disposed **only** in `GoogleMapsGisLayer`; capabilities never see it. | Pass |

No violations — **Complexity Tracking is empty**.

**Post-design re-check**: the design adds one field to a published handle (`overlayGroup`) and one registry bind method. The T036 isolation test must be extended, not weakened: an extension still cannot reach another extension's groups, and the new field grants no renderer/scene/camera access.

## Project Structure

### Documentation (this feature)

```text
specs/087-solar-dome-overlay-canvas/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── overlay-surface.md
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root, frontend only)

```text
src/AskLucy.Web/ClientApp/src/
├── viewer/
│   ├── layers/gis/
│   │   ├── GoogleMapsGisLayer.ts          # owns overlay surface + overlay scene/camera; onDraw renders both
│   │   ├── overlaySurface.ts              # NEW — canvas + second renderer: create / resize / dispose
│   │   └── overlaySurface.test.ts         # NEW — lifecycle, failure, injection
│   └── scene/
│       ├── DrawingSpaceRegistry.ts        # handle gains `overlayGroup`; `bindOverlay(scene)`
│       ├── DrawingSpaceRegistry.test.ts   # extended: overlay group parenting, release, isolation keys
│       ├── overlayCamera.ts               # NEW — pure: copy map transform + identity pose onto a camera
│       └── overlayCamera.test.ts          # NEW
└── features/solar/
    ├── scene/
    │   ├── SolarScene.ts                  # dome groups parented to overlayGroup; setVisible(); ground offset on both
    │   ├── SolarScene.overlay.test.ts     # NEW — ownership split (dome in, massing/shadows out)
    │   ├── sunPathCurve.ts                # remove occluder + depth hacks; normal depth sorting
    │   └── sunPathCurve.test.ts           # drop occluder/renderOrder/depthTest assertions; pin the new ones
    └── extensions/builtin/solarAnalysisExtension.tsx  # visibility via SolarScene.setVisible()
```

**Structure Decision**: frontend-only change inside the existing viewer/solar structure. No backend project, no new package. The second surface follows the viewer's existing bind-after-construction singletons convention (`DrawingSpaceRegistry.bind`, `RendererState.bind`, `RedrawScheduler.bind`, `activeScene.bind`).

## Phase 0 — Research

See [research.md](./research.md). Decisions D1–D9 cover: the separate surface, the capability seam, camera derivation, sizing/resize, renderer settings, lifecycle/disposal, deleting the workarounds, failure containment, and test seams.

## Phase 1 — Design & Contracts

- Entities and ownership: [data-model.md](./data-model.md)
- Internal contract: [contracts/overlay-surface.md](./contracts/overlay-surface.md)
- Validation: [quickstart.md](./quickstart.md)

## Complexity Tracking

No constitution violations; nothing to justify.
