# Contract: Viewer Engine Command/Event API

Satisfies spec FR-021–FR-024 (User Story 6). This is the internal, in-process TypeScript API the
`viewer/engine` module exposes to its own React host (this feature) and, unmodified, to a future
Ask Lucy AI-agent integration (not built in this feature). It is not an HTTP API — it lives entirely in
the browser.

## `IViewerEngine`

```ts
interface ViewerCommandResult<T = void> {
  ok: boolean
  data?: T
  error?: string // present when ok === false; caller-visible, never a silent no-op (FR-022)
}

interface IViewerEngine {
  // Layers (FR-002, FR-003, FR-021)
  addLayer(layer: RenderLayerInput): ViewerCommandResult<{ layerId: string }>
  removeLayer(layerId: string): ViewerCommandResult
  setLayerVisibility(layerId: string, visible: boolean): ViewerCommandResult

  // Camera / navigation (FR-013–FR-017, FR-021)
  zoomToLocation(latitude: number, longitude: number, zoom?: number): ViewerCommandResult
  setViewMode(mode: 'isometric' | 'plan'): ViewerCommandResult
  setRotationEnabled(enabled: boolean): ViewerCommandResult

  // Selection / highlighting (FR-018, FR-019, FR-021)
  select(layerId: string, elementId: string): ViewerCommandResult
  clearSelection(): ViewerCommandResult

  // Content / overlays (FR-020, FR-021)
  displayContent(layerId: string, content: unknown): ViewerCommandResult
  createOverlay(overlay: OverlayInput): ViewerCommandResult<{ overlayId: string }>

  // Events (FR-023)
  on<E extends ViewerEvent['type']>(type: E, handler: (event: Extract<ViewerEvent, { type: E }>) => void): () => void // returns unsubscribe
}
```

## Commands

Every command is synchronous-or-Promise-returning and **always** resolves to a `ViewerCommandResult` —
it never throws for an expected failure (unknown layer id, invalid coordinates, unsupported content
type). This satisfies FR-022 ("invalid or unavailable parameters MUST produce a caller-visible failure
rather than a silent no-op") and this codebase's broader no-silent-failures rule at the API boundary.

| Command | Failure examples (`ok: false`) |
|---|---|
| `addLayer` | Duplicate `id` already registered |
| `removeLayer` | Unknown `layerId` |
| `setLayerVisibility` | Unknown `layerId` |
| `zoomToLocation` | Coordinates out of range |
| `setViewMode` | N/A (always valid — two closed values) |
| `setRotationEnabled` | N/A (always valid — boolean) |
| `select` | Unknown `layerId`/`elementId` |
| `clearSelection` | N/A (always valid — no-op if already empty) |
| `displayContent` | Unknown `layerId`, or `layerId` refers to a layer kind that does not support the given content shape (spec Edge Cases: "a future content type is not yet implemented") |
| `createOverlay` | Invalid overlay definition |

## Events

| Event | Payload | Fired when |
|---|---|---|
| `layerAdded` | `{ layerId, kind }` | `addLayer` succeeds |
| `layerRemoved` | `{ layerId }` | `removeLayer` succeeds |
| `contentLoaded` | `{ layerId }` | A layer's content (e.g. the map tiles for the current-location `GisMapLayer`) finishes loading |
| `selectionChanged` | `{ layerId, elementId } \| null` | `select`/`clearSelection` succeeds, or selection is cleared by the deterministic overlap-resolution rule (spec Edge Cases) |
| `viewModeChanged` | `{ mode: 'isometric' \| 'plan' }` | `setViewMode` succeeds |
| `rotationChanged` | `{ enabled: boolean }` | `setRotationEnabled` succeeds, or rotation auto-stops for reduced-motion (FR-016) |

## Verification (US6, no AI agent required)

Every command above MUST be independently callable and observably correct without any AI agent
connected (SC-006) — e.g., from a test harness or the browser devtools console:

```ts
const unsubscribe = viewerEngine.on('viewModeChanged', console.log)
viewerEngine.setViewMode('plan') // → logs { mode: 'plan' }, returns { ok: true }
viewerEngine.select('nonexistent-layer', 'x') // → { ok: false, error: '...' }, no event fires
```
