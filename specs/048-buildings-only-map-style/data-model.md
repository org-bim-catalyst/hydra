# Phase 1 Data Model: Buildings-Only Map Style

No persisted or backend entities are introduced — this feature is entirely in-memory
client state, extending an existing type.

## `MapStyleId` (extended)

`src/AskLucy.Web/ClientApp/src/viewer/api/commands.ts`

| Value | Meaning | New? |
|---|---|---|
| `'roadmap'` | Google's standard road map base rendering | existing |
| `'satellite'` | Satellite imagery, no labels | existing |
| `'hybrid'` | Satellite imagery with road/label overlay | existing |
| `'buildings-only'` | Roadmap base with roads, POI, transit, administrative, and natural landscape hidden | **new** |

Held in `viewerEngineStore.mapStyle: MapStyleId` (default `'roadmap'`, unchanged).
Read/written only through `viewerEngine.setMapStyle(style)` — no direct store mutation
from UI code, consistent with the existing three values.

## Map style rendering capability (new, derived — not persisted)

Not a stored entity; a derived boolean computed once at map-creation time from whether a
Map ID is configured (`import.meta.env.VITE_GOOGLE_MAPS_MAP_ID`, per research.md
Decision 2):

- `raster rendering active` → `'buildings-only'` is offered in the map style menu.
- `vector rendering active` (Map ID configured) → `'buildings-only'` is omitted from the
  menu's action list entirely (FR-006); the other three styles are unaffected.

This flag does not need to live in `viewerEngineStore` as reactive state — it cannot
change during a session (the env var is fixed at build time, and `MapRenderTarget`
already treats `mapId` as fixed for the lifetime of a mounted map). It is read once by
`MapRenderTarget`/the module that decides `mapId` and passed down to
`workspaceControls.tsx` as a plain value (e.g. exported alongside the existing
`viewerEngine` singleton, or as a prop into `useMapStyleControl` — finalized in
tasks.md), not modeled as new store state.

## State transitions

Identical machine to the existing three styles, with one more state:

```
roadmap ⇄ satellite ⇄ hybrid ⇄ buildings-only  (any → any, single click, no intermediate state)
```

`buildings-only` is reachable/leaveable via the exact same `selectStyle` call the other
three use — no new transition rules, guards, or async steps.
