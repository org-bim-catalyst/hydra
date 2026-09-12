import type { RenderLayerKind } from './layers'
import type { CameraState, CameraViewMode, MapStyleId } from './commands'
import type { ContentFailureReason } from '../content/ViewerContent'
import type { DrawingRequirement } from '../scene/rendererState'

/** contracts/viewer-engine-api.md — every notification the viewer emits for external observers
 * (this feature's own UI today; a future AI agent or analytics later, per FR-023/FR-024). */
export type ViewerEvent =
  | { type: 'layerAdded'; layerId: string; kind: RenderLayerKind }
  | { type: 'layerRemoved'; layerId: string }
  | { type: 'contentLoaded'; layerId: string }
  | { type: 'selectionChanged'; layerId: string | null; elementId: string | null }
  | { type: 'viewModeChanged'; mode: CameraViewMode }
  | { type: 'mapStyleChanged'; mapStyle: MapStyleId }
  | { type: 'rotationChanged'; enabled: boolean }
  // specs/051-viewer-scene-content-api — additive only (FR-038, FR-039). `contentLoaded` above is
  // deliberately reused for content loaded through this feature's commands too, rather than
  // duplicated (contracts/viewer-engine-api-extensions.md).
  | { type: 'contentLoading'; contentId: string }
  | { type: 'contentFailed'; contentId: string; reason: ContentFailureReason }
  | { type: 'cameraChanged'; camera: CameraState }
  | { type: 'drawingRequirementConflict'; requirement: DrawingRequirement; requestedBy: string[] }
  /** FR-019, constitution §2.VIII — a capability's `onFrame` callback (or other drawing-space
   * code invoked on its behalf) threw; contained by `DrawingSpaceRegistry`, never left to stop
   * another capability's callback or the render loop (research D3a). */
  | { type: 'drawingCallbackFailed'; extensionId: string; message: string }

export type ViewerEventType = ViewerEvent['type']

export type ViewerEventHandler<E extends ViewerEventType> = (
  event: Extract<ViewerEvent, { type: E }>,
) => void
