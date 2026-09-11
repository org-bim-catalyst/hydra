import type { OverlayInput, RenderLayerInput } from './layers'

/** spec.md FR-013 (as revised): the toolbar's isometric/plan camera perspective toggle. */
export type CameraViewMode = 'isometric' | 'plan'

/** The map/GIS content mode's base rendering style — mirrors `google.maps.MapTypeId`'s
 * ROADMAP/SATELLITE/HYBRID values. TERRAIN is intentionally omitted — no control surfaces it.
 * `'buildings-only'` (specs/048-buildings-only-map-style) is not itself a `MapTypeId` — it is a
 * ROADMAP base with a custom `google.maps.MapTypeStyle[]` layered on top
 * (`GoogleMapsGisLayer.BUILDINGS_ONLY_STYLE`) that hides roads/POI/transit/administrative/natural
 * landscape so building footprints dominate. */
export type MapStyleId = 'roadmap' | 'satellite' | 'hybrid' | 'buildings-only'

/** specs/048-buildings-only-map-style research.md Decision 2: custom `google.maps.MapTypeStyle`
 * JSON styling has no effect on Google's vector base-map rendering (active when a Map ID is
 * configured — see `GoogleMapsGisLayer.mapId`'s doc comment) — cloud-configured styling would be
 * required there instead. Rather than silently offering a "Buildings only" option that does
 * nothing on a vector deployment, callers (the map style menu) MUST check this before offering
 * the option. Decided statically from the same build-time env var `MapRenderTarget` already uses
 * to decide whether to pass `mapId` to `google.maps.Map`, since that is this codebase's existing
 * proxy for "is this deployment on the vector rendering path." */
export function isBuildingsOnlyStyleSupported(): boolean {
  return !import.meta.env.VITE_GOOGLE_MAPS_MAP_ID
}

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
