import type { OverlayInput, RenderLayerInput } from './layers'
import type { CameraViewMode, MapStyleId, ViewerCommandResult } from './commands'
import type { ViewerEventHandler, ViewerEventType } from './events'

/** contracts/viewer-engine-api.md — the viewer's public command/event facade. Implemented by
 * `viewer/engine/ViewerEngine.ts`; every method resolves to a `ViewerCommandResult` rather than
 * throwing (FR-022), and state changes are observable via `on()` (FR-023). This is the exact
 * surface a future Ask Lucy AI-agent integration will call — unmodified by that later feature
 * (FR-024). */
export interface IViewerEngine {
  addLayer(layer: RenderLayerInput): ViewerCommandResult<{ layerId: string }>
  removeLayer(layerId: string): ViewerCommandResult
  setLayerVisibility(layerId: string, visible: boolean): ViewerCommandResult
  zoomToLocation(latitude: number, longitude: number, zoom?: number): ViewerCommandResult
  setViewMode(mode: CameraViewMode): ViewerCommandResult
  setMapStyle(mapStyle: MapStyleId): ViewerCommandResult
  setRotationEnabled(enabled: boolean): ViewerCommandResult
  select(layerId: string, elementId: string): ViewerCommandResult
  clearSelection(): ViewerCommandResult
  displayContent(layerId: string, content: unknown): ViewerCommandResult
  createOverlay(overlay: OverlayInput): ViewerCommandResult<{ overlayId: string }>
  on<E extends ViewerEventType>(type: E, handler: ViewerEventHandler<E>): () => void
}
