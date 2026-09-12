import type { OverlayInput, RenderLayerInput } from './layers'
import type { CameraViewMode, CameraState, MapStyleId, ViewerCommandResult } from './commands'
import type { ViewerEventHandler, ViewerEventType } from './events'
import type { ContentSource, ViewerContent, WorldPlacement } from '../content/ViewerContent'
import type { ReferencePoint } from '../scene/SceneAnchor'
import type { ElementProperties } from '../elements/elementIndex'

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

  // specs/051-viewer-scene-content-api — additive only (FR-038, FR-039); every method above is
  // unchanged. See contracts/viewer-engine-api-extensions.md.
  loadContent(source: ContentSource, placement?: WorldPlacement): ViewerCommandResult<{ contentId: string }>
  replaceContent(contentId: string, source: ContentSource, placement?: WorldPlacement): ViewerCommandResult
  unloadContent(contentId: string): ViewerCommandResult
  listContent(): ViewerCommandResult<{ content: ViewerContent[] }>
  getReferencePoint(): ViewerCommandResult<{ referencePoint: ReferencePoint | null }>
  getCameraState(): ViewerCommandResult<{ camera: CameraState }>
  getElementInfo(layerId: string, elementId: string): ViewerCommandResult<{ info: ElementProperties }>
  selectAndFrame(layerId: string, elementId: string): ViewerCommandResult
  /** Not a `ViewerCommandResult` command like the others — a fire-and-forget scheduling request
   * (research D4). Safe to call with nothing pending; safe to call after the requesting
   * capability has stopped (FR-024). */
  invalidate(): void
}
