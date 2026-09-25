import { create } from 'zustand'
import type { ViewportBounds } from '../features/viewer/types/ViewportBounds'
import type { SiteBoundaryConfidenceLevel } from './activeSiteBoundaryStore'

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
  /** The server's Low/Medium/High reading of that confirmation — what the site card shows the
   * moment the place is confirmed, until the outline's own level replaces it. Null for
   * geolocation-sourced locations. */
  confidenceLevel: SiteBoundaryConfidenceLevel | null
  /** The server's one-sentence reason for that level — the site card's shield tooltip. Null for
   * geolocation-sourced locations. */
  confidenceReason: string | null
  /** Google Maps location_type ("ROOFTOP", "GEOMETRIC_CENTER", etc.). Null for geolocation-sourced
   * locations or when the provider does not return it (specs/038-viewer-poi-zoom). */
  locationType: string | null
  /** Geocoding bounding box. Used by ViewerSurface to call fitBounds for accurate zoom level
   * (specs/038-viewer-poi-zoom). Null for geolocation-sourced locations or when absent from the
   * geocoding response. */
  viewport: ViewportBounds | null
}

/**
 * How far a later device fix must land from the established one before it is treated as the user
 * being somewhere else rather than the same place re-reported.
 *
 * `useGeolocation` keeps a low-accuracy `watchPosition` running for the whole session to detect
 * revocation, so fixes keep arriving indefinitely, disagreeing with each other by however much
 * WiFi/GPS happen to disagree that minute. Every such update used to be written straight through.
 * `ViewerSurface` already refuses to move the *camera* on them ("a fresh reading of the place
 * already shown is not a reason to take it back") — but the scene's reference point had no such
 * rule, and since specs/051 that point is the origin every drawn thing is positioned from. A
 * 30-metre re-fix therefore slid the whole solar analysis — dome, dial, footprints, boundary ring
 * — that far across a basemap that had not moved at all, seconds after the user did something
 * unrelated. It also re-keyed the solar site, re-resolving its time zone and re-fetching its
 * buildings each time.
 *
 * 500 m is chosen to sit above any disagreement between two fixes *of the same place* — including
 * the large one between an IP/WiFi estimate and the GPS fix that supersedes it, which this
 * deliberately still lets through as a single corrective move — and far below any distance at
 * which the user is meaningfully somewhere else. A genuine relocation still updates.
 */
export const GEOLOCATION_RELOCATION_THRESHOLD_METRES = 500

const EARTH_RADIUS_METRES = 6_371_008.8

/** Equirectangular approximation — exact enough by orders of magnitude at the scale being
 * compared against, and it avoids pulling the viewer's coordinate frame into a plain store. */
function approximateDistanceMetres(
  fromLatitude: number,
  fromLongitude: number,
  toLatitude: number,
  toLongitude: number,
): number {
  const toRadians = Math.PI / 180
  const meanLatitude = ((fromLatitude + toLatitude) / 2) * toRadians
  const deltaLatitude = (toLatitude - fromLatitude) * toRadians
  const deltaLongitude = (toLongitude - fromLongitude) * toRadians * Math.cos(meanLatitude)
  return Math.hypot(deltaLatitude, deltaLongitude) * EARTH_RADIUS_METRES
}

interface ActiveLocationActions {
  /** Sets the active location from device geolocation. No-op when source === 'agent' (FR-012), and
   * a no-op for a re-report of the place already shown (see the threshold above). */
  setFromGeolocation(latitude: number, longitude: number): void
  /** Sets the active location from an agent-confirmed resolution. Always wins (FR-012).
   * specs/038-viewer-poi-zoom: extended with optional locationType and viewport. */
  setFromAgent(
    latitude: number,
    longitude: number,
    locationName: string,
    confidence: number,
    locationType?: string | null,
    viewport?: ViewportBounds | null,
    confidenceLevel?: SiteBoundaryConfidenceLevel | null,
    confidenceReason?: string | null,
  ): void
  /** Updates locationName once the weather API response arrives. Only applies when coordinates
   * still match the current active location — guards against a stale weather response landing
   * after a location change — and never to an agent-confirmed location, whose name is the
   * agent's own. */
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
    confidenceLevel: null,
    confidenceReason: null,
    locationType: null,
    viewport: null,

    setFromGeolocation(latitude, longitude) {
      const current = get()
      // FR-012: agent-confirmed location is higher priority — startup detection cannot displace it.
      if (current.source === 'agent') return
      // The device establishes a location once and then keeps reporting it. Writing every report
      // through moved everything anchored to the reference point; see the threshold's own note.
      if (
        current.source === 'geolocation' &&
        current.latitude !== null &&
        current.longitude !== null &&
        approximateDistanceMetres(current.latitude, current.longitude, latitude, longitude) <
          GEOLOCATION_RELOCATION_THRESHOLD_METRES
      ) {
        return
      }
      set({
        source: 'geolocation',
        latitude,
        longitude,
        confidence: null,
        confidenceLevel: null,
        confidenceReason: null,
        locationType: null,
        viewport: null,
      })
    },

    setFromAgent(
      latitude,
      longitude,
      locationName,
      confidence,
      locationType = null,
      viewport = null,
      confidenceLevel = null,
      confidenceReason = null,
    ) {
      set({ source: 'agent', latitude, longitude, locationName, confidence, confidenceLevel, confidenceReason, locationType, viewport })
    },

    setLocationName(latitude, longitude, locationName) {
      const s = get()
      if (s.latitude !== latitude || s.longitude !== longitude) return
      // The agent named the place it resolved; the weather lookup's reverse-geocoded area name
      // ("Umm Suqeim, Dubai") is a coarser answer to the same question and must not replace it.
      if (s.source === 'agent') return
      set({ locationName })
    },

    clear() {
      set({
        source: null,
        latitude: null,
        longitude: null,
        locationName: null,
        confidence: null,
        confidenceLevel: null,
        confidenceReason: null,
        locationType: null,
        viewport: null,
      })
    },
  }),
)
