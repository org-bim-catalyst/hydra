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

/** specs/048-buildings-only-map-style research.md Decision 2 (superseded by Decision 4): custom
 * `google.maps.MapTypeStyle` JSON styling has no effect on Google's vector base-map rendering
 * (active when a Map ID is configured — see `GoogleMapsGisLayer.mapId`'s doc comment). On a
 * raster deployment (no Map ID) that's a non-issue — the client-side style just works. On a
 * vector deployment, "Buildings only" is only offered once a *second*, cloud-styled Map ID
 * (`VITE_GOOGLE_MAPS_BUILDINGS_ONLY_MAP_ID`, see `docs/google-maps-styles/`) is configured for
 * `MapRenderTarget` to switch to — never silently offered as a selectable option that would do
 * nothing (FR-006). Decided statically from build-time env vars, mirroring how `MapRenderTarget`
 * already decides the base `mapId`. */
export function isBuildingsOnlyStyleSupported(): boolean {
  const hasBaseMapId = Boolean(import.meta.env.VITE_GOOGLE_MAPS_MAP_ID)
  const hasBuildingsOnlyMapId = Boolean(import.meta.env.VITE_GOOGLE_MAPS_BUILDINGS_ONLY_MAP_ID)
  return !hasBaseMapId || hasBuildingsOnlyMapId
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
