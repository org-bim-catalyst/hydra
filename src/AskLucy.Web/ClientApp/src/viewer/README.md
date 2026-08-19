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

## Manual verification (no AI agent required)

In a development build, the running engine is exposed as `window.__askLucyViewerEngine` — open
devtools on `/studio` and try:

```js
window.__askLucyViewerEngine.setViewMode('plan')
window.__askLucyViewerEngine.select('gis-current-location', 'current-location')
```

See `specs/027-immersive-viewer-platform/quickstart.md` Scenario 5 for the full walkthrough.
