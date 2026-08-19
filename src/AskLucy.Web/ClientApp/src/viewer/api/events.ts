import type { RenderLayerKind } from './layers'
import type { CameraViewMode } from './commands'

/** contracts/viewer-engine-api.md — every notification the viewer emits for external observers
 * (this feature's own UI today; a future AI agent or analytics later, per FR-023/FR-024). */
export type ViewerEvent =
  | { type: 'layerAdded'; layerId: string; kind: RenderLayerKind }
  | { type: 'layerRemoved'; layerId: string }
  | { type: 'contentLoaded'; layerId: string }
  | { type: 'selectionChanged'; layerId: string | null; elementId: string | null }
  | { type: 'viewModeChanged'; mode: CameraViewMode }
  | { type: 'rotationChanged'; enabled: boolean }

export type ViewerEventType = ViewerEvent['type']

export type ViewerEventHandler<E extends ViewerEventType> = (
  event: Extract<ViewerEvent, { type: E }>,
) => void
