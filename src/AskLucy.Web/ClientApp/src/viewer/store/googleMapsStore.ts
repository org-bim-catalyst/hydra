import { create } from 'zustand'

/** Holds the live `google.maps.Map` instance created by `MapRenderTarget` → `createGoogleMapsGisLayer`.
 * Populated when the map initializes, cleared on unmount. Used by `POIMarkerOverlay`
 * (specs/038-viewer-poi-zoom) to create and position markers without the marker needing to know
 * about `MapRenderTarget`'s internals. */
interface GoogleMapsState {
  map: google.maps.Map | null
  setMap: (map: google.maps.Map | null) => void
}

export const useGoogleMapsStore = create<GoogleMapsState>()((set) => ({
  map: null,
  setMap: (map) => set({ map }),
}))
