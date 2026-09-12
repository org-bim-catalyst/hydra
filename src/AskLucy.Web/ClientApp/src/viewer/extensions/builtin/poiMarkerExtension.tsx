import { POIMarkerOverlay } from '../../../features/viewer/components/POIMarkerOverlay'
import type { ExtensionContext } from '../context'
import { viewerExtensionRegistry } from '../registry'
import type { ViewerExtension } from '../ViewerExtension'

/** research D8 — contributes the existing `POIMarkerOverlay` unchanged; it already no-ops until
 * `googleMapsStore.map` populates, and manages its marker entirely through `useEffect`, so
 * `stop()` needs nothing beyond withdrawing the contribution the context already tracks. */
export const poiMarkerExtension: ViewerExtension = {
  id: 'viewer.poi-marker',
  manifest: { displayName: 'POI Marker', description: 'Marks the agent-confirmed active location on the map.' },
  start(context: ExtensionContext) {
    context.contributeOverlay(POIMarkerOverlay)
  },
  stop() {},
}

viewerExtensionRegistry.register(poiMarkerExtension)
