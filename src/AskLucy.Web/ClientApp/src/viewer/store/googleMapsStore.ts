import { create } from 'zustand'
import type { GoogleMapsGisLayerHandle } from '../layers/gis/GoogleMapsGisLayer'

/** Holds the live `google.maps.Map` instance (and, since specs/042-site-boundary-resolution,
 * the full `GoogleMapsGisLayerHandle`) created by `MapRenderTarget` → `createGoogleMapsGisLayer`.
 * Populated when the map initializes, cleared on unmount. Used by `POIMarkerOverlay`
 * (specs/038-viewer-poi-zoom) and `SiteBoundaryOverlay` (specs/042) to reach the map/handle
 * without either needing to know about `MapRenderTarget`'s internals. */
interface GoogleMapsState {
  map: google.maps.Map | null
  handle: GoogleMapsGisLayerHandle | null
  setMap: (map: google.maps.Map | null) => void
  setHandle: (handle: GoogleMapsGisLayerHandle | null) => void
}

export const useGoogleMapsStore = create<GoogleMapsState>()((set) => ({
  map: null,
  handle: null,
  setMap: (map) => set({ map }),
  setHandle: (handle) => set({ handle }),
}))
