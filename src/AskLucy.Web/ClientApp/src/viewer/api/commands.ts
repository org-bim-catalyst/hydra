import type { OverlayInput, RenderLayerInput } from './layers'

/** spec.md FR-013 (as revised): the toolbar's isometric/plan camera perspective toggle. */
export type CameraViewMode = 'isometric' | 'plan'

/** The map/GIS content mode's base rendering style — mirrors `google.maps.MapTypeId`'s
 * ROADMAP/SATELLITE/HYBRID values. TERRAIN is intentionally omitted — no control surfaces it. */
export type MapStyleId = 'roadmap' | 'satellite' | 'hybrid'

/** contracts/viewer-engine-api.md — every outcome the viewer's command surface can produce.
 * Always resolves; a command never throws for an expected failure (FR-022). */
export interface ViewerCommandResult<T = void> {
  ok: boolean
  data?: T
  error?: string
}

/** data-model.md "Viewer Command" — the vocabulary `IViewerEngine`'s methods implement. Kept as
 * a discriminated union alongside the concrete interface (viewer/api/engine.ts) so the full
 * command set is inspectable/documentable as data, not only as method signatures. */
export type ViewerCommand =
  | { type: 'addLayer'; layer: RenderLayerInput }
  | { type: 'removeLayer'; layerId: string }
  | { type: 'setLayerVisibility'; layerId: string; visible: boolean }
  | { type: 'zoomToLocation'; latitude: number; longitude: number; zoom?: number }
  | { type: 'setViewMode'; mode: CameraViewMode }
  | { type: 'setMapStyle'; mapStyle: MapStyleId }
  | { type: 'setRotationEnabled'; enabled: boolean }
  | { type: 'select'; layerId: string; elementId: string }
  | { type: 'clearSelection' }
  | { type: 'displayContent'; layerId: string; content: unknown }
  | { type: 'createOverlay'; overlay: OverlayInput }
