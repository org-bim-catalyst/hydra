# Implementation Plan: Buildings-Only Map Style

**Branch**: `048-buildings-only-map-style` | **Date**: 2026-09-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/048-buildings-only-map-style/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Add a fourth map style, "buildings only", to the existing roadmap/satellite/hybrid
toggle in the map/GIS content mode. Selecting it applies a `google.maps.MapTypeStyle[]`
array (via `map.setOptions({ styles })`) that hides roads, POI, transit, administrative
labels/borders, and natural landscape, layered on top of the existing roadmap base type
rather than replacing `MapTypeId`. Custom JSON styling has no effect on Google's vector
rendering path (active when a Map ID is configured), so the option is only exposed when
the deployment is using raster rendering (no Map ID configured) — determined statically
from the existing `VITE_GOOGLE_MAPS_MAP_ID` build-time env var already used to decide
whether to pass `mapId` to `google.maps.Map`.

## Technical Context

**Language/Version**: TypeScript 5.x (React 18, Vite) — `src/AskLucy.Web/ClientApp`

**Primary Dependencies**: `@googlemaps/js-api-loader`, `google.maps` JS API (Maps
JavaScript API, already loaded lazily by `GoogleMapsGisLayer`), Zustand
(`viewerEngineStore`), `@remixicon/react` (menu icon)

**Storage**: N/A — in-memory view state only (`viewerEngineStore.mapStyle`), same as the
existing three styles; no persistence across reload

**Testing**: Vitest + Testing Library (unit tests on `ViewerEngine`, `viewerEngineStore`,
`GoogleMapsGisLayer`, `workspaceControls`), matching existing test files for these modules

**Target Platform**: Browser (Ask Lucy web client), map/GIS content mode of the viewer

**Project Type**: Web application (existing `frontend`-only change — no backend/API
surface is touched)

**Performance Goals**: Style switch must feel instant (single synchronous
`map.setOptions`/`map.setMapTypeId` call, no network round-trip) — matches the existing
three styles' behavior

**Constraints**: Must not alter `MapStyleId`'s existing three values' behavior; must not
regress the vector-rendering deployment path (Map ID configured) by offering an option
that would silently no-op there (FR-006)

**Scale/Scope**: One new enum value, one new UI action, one new style-application branch
in `GoogleMapsGisLayer`, one new capability flag threaded from `MapRenderTarget`/env
config through to the menu. No new files beyond tests; touches the same five files the
existing map-style architecture already spans.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture & Dependency Rule**: N/A to this change — it is entirely
  within the frontend's `viewer/` presentation layer; no backend layer is touched. PASS.
- **II. SOLID**: Extends the existing `MapStyleId` union and the existing
  `setMapTypeId`/`applyMapStyle` seam rather than introducing a parallel mechanism —
  Open/Closed is respected (new variant added to an existing switch-like mapping, no
  existing call sites change shape). PASS.
- **III. Simplicity First (DRY/KISS/YAGNI)**: Reuses the existing single-source-of-truth
  flow (`viewerEngine.setMapStyle` → store → `GoogleMapsGisLayer`) with no new store,
  no new command channel, and no speculative generalization (e.g., no arbitrary custom
  style editor) beyond the one named style the spec asks for. PASS.
- **Provider neutrality / vendor lock-in principles**: N/A — this is a Google Maps
  rendering detail already fully encapsulated inside `GoogleMapsGisLayer`; no AI
  provider is involved. PASS.
- **Transparency**: FR-006 (don't offer the option where it would silently no-op) is the
  applicable instance of the constitution's general "no silently broken feature" spirit
  for this frontend-only feature. PASS.

No violations identified — Complexity Tracking table is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/
├── viewer/
│   ├── api/
│   │   ├── commands.ts              # MapStyleId union: add 'buildings-only'
│   │   ├── engine.ts                # ViewerEngine interface — no shape change
│   │   └── events.ts                # mapStyleChanged event — no shape change
│   ├── store/
│   │   ├── viewerEngineStore.ts     # mapStyle state — no shape change (same MapStyleId)
│   │   └── viewerEngineStore.test.ts
│   ├── engine/
│   │   ├── ViewerEngine.ts          # setMapStyle — no shape change
│   │   ├── ViewerEngine.test.ts
│   │   ├── ViewerEngine.contract.test.ts
│   │   ├── MapRenderTarget.tsx      # passes mapStyle capability (raster vs vector) down
│   │   └── MapRenderTarget.test.tsx
│   └── layers/gis/
│       ├── GoogleMapsGisLayer.ts    # setMapTypeId: add buildings-only branch (map.setOptions styles)
│       └── GoogleMapsGisLayer.test.ts
└── features/chat/
    └── workspaceControls.tsx        # useMapStyleControl: add "Buildings only" action, gated by capability
```

**Structure Decision**: Single existing web application (`src/AskLucy.Web/ClientApp`,
React + Vite). No new top-level directories — this feature extends the existing map
style vertical slice (`viewer/api` → `viewer/store` → `viewer/engine` →
`viewer/layers/gis` → `features/chat/workspaceControls.tsx`) in place, following the
same file set the existing three styles already touch.

## Complexity Tracking

No Constitution Check violations — this section is not applicable.
