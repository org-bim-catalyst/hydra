import { create } from 'zustand'

type ActiveLocationSource = 'geolocation' | 'agent'

interface ActiveLocationState {
  source: ActiveLocationSource | null
  latitude: number | null
  longitude: number | null
  /** Human-readable place name. Populated by the weather API response for geolocation-sourced
   * locations (via setLocationName), or by the agent's resolved name for agent-confirmed
   * locations. Null until first weather snapshot arrives (geolocation path). */
  locationName: string | null
  /** Agent confidence score. Null for geolocation-sourced locations. */
  confidence: number | null
}

interface ActiveLocationActions {
  /** Sets the active location from device geolocation. No-op when source === 'agent' (FR-012). */
  setFromGeolocation(latitude: number, longitude: number): void
  /** Sets the active location from an agent-confirmed resolution. Always wins (FR-012). */
  setFromAgent(latitude: number, longitude: number, locationName: string, confidence: number): void
  /** Updates locationName once the weather API response arrives. Only applies when coordinates
   * still match the current active location — guards against a stale weather response landing
   * after a location change. */
  setLocationName(latitude: number, longitude: number, locationName: string): void
  /** Resets to no-location state (e.g. permission denied, revoked mid-session). After clear(),
   * setFromGeolocation can re-establish a location (FR-012 revocation recovery). */
  clear(): void
}

export const useActiveLocationStore = create<ActiveLocationState & ActiveLocationActions>()(
  (set, get) => ({
    source: null,
    latitude: null,
    longitude: null,
    locationName: null,
    confidence: null,

    setFromGeolocation(latitude, longitude) {
      // FR-012: agent-confirmed location is higher priority — startup detection cannot displace it.
      if (get().source === 'agent') return
      set({ source: 'geolocation', latitude, longitude, confidence: null })
    },

    setFromAgent(latitude, longitude, locationName, confidence) {
      set({ source: 'agent', latitude, longitude, locationName, confidence })
    },

    setLocationName(latitude, longitude, locationName) {
      const s = get()
      if (s.latitude !== latitude || s.longitude !== longitude) return
      set({ locationName })
    },

    clear() {
      set({ source: null, latitude: null, longitude: null, locationName: null, confidence: null })
    },
  }),
)
