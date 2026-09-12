# Viewer Engine

The extensible viewer platform behind the Flumeria workspace (specs/027-immersive-viewer-platform).
See `specs/027-immersive-viewer-platform/` for the full spec, plan, research, data model, and
contracts this package implements — this file is a quick orientation, not a duplicate of those.

## Layout

- `api/` — the typed contracts: `layers.ts` (`RenderLayer`), `commands.ts` (`ViewerCommand`/
  `ViewerCommandResult`), `events.ts` (`ViewerEvent`), `engine.ts` (`IViewerEngine`). Mirrors
  `contracts/viewer-engine-api.md` exactly.
- `engine/` — `ViewerEngine` (the facade implementing `IViewerEngine`), `viewerEventBus.ts`
  (pub/sub), `viewerEngineInstance.ts` (the shared singleton), `PlaceholderRenderTarget.tsx`/
  `ViewerFallback.tsx`/`MapRenderTarget.tsx` (the three things the viewer can currently show).
- `camera/` — isometric/plan view-mode application and continuous-rotation driving, applied to
  whichever real render target is active.
- `layers/gis/` — `GoogleMapsGisLayer.ts`, bridging a Google Maps `WebGLOverlayView` to a
  Three.js scene (research.md Decision 3).
- `layers/model/` — reserved for future model/drawing content (contract-only today).
- `selection/` — `resolveSelection.ts`, the deterministic overlap-resolution rule.
- `overlays/` — the `Overlay` type alias for `RenderLayer`s with `kind: 'overlay'`.
- `store/` — `viewerEngineStore.ts`, the session-scoped Zustand store `ViewerEngine` reads/writes.

## Using the viewer from a future AI-agent integration

Everything an agent needs is `viewerEngine` (`engine/viewerEngineInstance.ts`) and the types in
`api/`. Every command returns a `ViewerCommandResult` — check `.ok` before trusting `.data`, and
never assume a command throws instead of failing gracefully (it won't). Subscribe to events with
`viewerEngine.on(type, handler)` (returns an unsubscribe function) rather than polling state.

```ts
import { viewerEngine } from './engine/viewerEngineInstance'

const result = viewerEngine.addLayer({ kind: 'gis', metadata: { center: { latitude, longitude } } })
if (!result.ok) {
  // handle result.error — never assume success
}

const unsubscribe = viewerEngine.on('selectionChanged', (event) => {
  console.log('Selection changed:', event.layerId, event.elementId)
})
```

## Commands

| Command | Signature | Notes |
|---|---|---|
| `addLayer` | `(layer: RenderLayerInput) => ViewerCommandResult<{ layerId: string }>` | Fails on a duplicate id. |
| `removeLayer` | `(layerId: string) => ViewerCommandResult` | Fails if the layer doesn't exist. |
| `setLayerVisibility` | `(layerId: string, visible: boolean) => ViewerCommandResult` | Fails if the layer doesn't exist. |
| `zoomToLocation` | `(latitude: number, longitude: number, zoom?: number) => ViewerCommandResult` | Fails on out-of-range coordinates; succeeds as a no-op if no real content is active. |
| `setViewMode` | `(mode: 'isometric' \| 'plan') => ViewerCommandResult` | Always succeeds; no visible effect on the placeholder (FR-013). |
| `setRotationEnabled` | `(enabled: boolean) => ViewerCommandResult` | Always succeeds; no visible effect on the placeholder (FR-017). |
| `select` | `(layerId: string, elementId: string) => ViewerCommandResult` | Fails unless the element was registered via `registerSelectableElement` by the layer that owns it. |
| `clearSelection` | `() => ViewerCommandResult` | Always succeeds. |
| `displayContent` | `(layerId: string, content: unknown) => ViewerCommandResult` | Fails if the layer doesn't exist, or `content` is null/undefined. |
| `createOverlay` | `(overlay: OverlayInput) => ViewerCommandResult<{ overlayId: string }>` | Fails on a duplicate id. |

## Events

`layerAdded`, `layerRemoved`, `contentLoaded`, `selectionChanged`, `viewModeChanged`,
`rotationChanged` — see `api/events.ts` for exact payload shapes.

## Panels (`panels/`)

The floating panel framework (specs/028), reshaped by specs/049 into a content model. See
`specs/049-panel-content-model/` for the full spec, plan, research, data model and contracts.

- `content/` — the panel content vocabulary. `blocks.ts` defines each block kind (`heading`,
  `text`, `keyValue`, `table`, `chart`, `metric`, `image`, `divider`) as a zod schema, plus the
  loose document envelope (`panelContentSchema`) that gates a whole document without deep-checking
  every block — that split is what lets one malformed block degrade individually instead of
  failing the entire panel. `ContentRenderer.tsx` renders an ordered block sequence; `blockRegistry.ts`
  is its internal kind→renderer map. Presenting a new kind of content Lucy has never shown before
  requires composing a different sequence of these blocks — never new code.
- `actions/` — the closed action allowlist (`allowlist.ts`) a block or block entry may invoke.
  Every command is an explicit, written-out mapping onto the viewer's published `IViewerEngine`
  surface — never dynamic dispatch by a content-supplied string, because panel content is composed
  by a language model and constitution §8 treats that as untrusted input. `ActionAffordance.tsx` is
  the shared presentation every actionable entry renders through; an action that fails validation
  is rendered inert, never merely refused on click.
- `chrome/` — `PanelChrome` (title bar / resizable / default size) and `resolveChrome`, applying a
  request's override on top of a base chrome and clamping to the minimum usable size.
- `registry.ts` — narrowed by specs/049 to hold only **live panel kinds**: panels whose content is
  code rather than data (continuous state, an owned drawing surface, or values flowing back into
  them live). No content panel needs registration. Nothing registers here today — the four
  built-in kinds this registry used to hold at import time became content blocks instead.
- `store/floatingPanelStore.ts` — owns every open panel's lifecycle: cascade placement, z-order,
  the fixed-cap LRU eviction, minimize/restore, viewport clamping, and the two viewer-context
  subscriptions. Branches on a request's `kind` (`content` vs `live`) at construction; everything
  after that is common to both.

Two capabilities give Lucy access to panels: `present_panel_content` (always available — composes
content freely from the vocabulary) and `open_live_panel` (available only while something has
registered a live kind; ships with no consumer in this feature).

## Manual verification (no AI agent required)

In a development build, the running engine is exposed as `window.__askLucyViewerEngine` — open
devtools on `/studio` and try:

```js
window.__askLucyViewerEngine.setViewMode('plan')
window.__askLucyViewerEngine.select('gis-current-location', 'current-location')
```

See `specs/027-immersive-viewer-platform/quickstart.md` Scenario 5 for the full walkthrough.
