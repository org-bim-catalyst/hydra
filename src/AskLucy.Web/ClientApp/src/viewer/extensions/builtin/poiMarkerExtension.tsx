import { POIMarkerOverlay } from '../../../features/viewer/components/POIMarkerOverlay'
import { SpatialLayersOverlay } from '../../../features/viewer/components/SpatialLayersOverlay'
import type { ExtensionContext } from '../context'
import { viewerExtensionRegistry } from '../registry'
import type { ViewerExtension } from '../ViewerExtension'

/** research D8 — contributes the existing `POIMarkerOverlay` unchanged; it already no-ops until
 * `googleMapsStore.map` populates, and manages its marker entirely through `useEffect`, so
 * `stop()` needs nothing beyond withdrawing the contribution the context already tracks.
 * specs/094 adds `SpatialLayersOverlay` beside it — same idiom, every visible spatial layer
 * (markers, buffers, area highlights) rather than the single active-location marker. */
export const poiMarkerExtension: ViewerExtension = {
  id: 'viewer.poi-marker',
  manifest: { displayName: 'Map Layers', description: 'Marks the agent-confirmed active location and draws every active spatial layer on the map.' },
  start(context: ExtensionContext) {
    context.contributeOverlay(POIMarkerOverlay)
    context.contributeOverlay(SpatialLayersOverlay)
  },
  stop() {},
}

viewerExtensionRegistry.register(poiMarkerExtension)
