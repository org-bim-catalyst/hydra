import { useEffect, useState } from 'react'

export type GeolocationStatus = 'resolving' | 'granted' | 'unavailable'

export interface GeolocationState {
  status: GeolocationStatus
  latitude: number | null
  longitude: number | null
}

/** Standard, not high, accuracy (research.md Decision 8) — city-level precision is enough to
 * center a map and look up weather, without the extra permission friction/latency/battery
 * cost of GPS-grade accuracy. */
const GEOLOCATION_TIMEOUT_MS = 10_000

/** FR-006: requests the user's current location via the browser Geolocation API. Denied,
 * unsupported, and timed-out are all treated identically as `'unavailable'` (spec.md Edge
 * Cases) — never surfaced as an error to the user (FR-008).
 *
 * Uses `watchPosition` rather than a one-shot `getCurrentPosition` call so a later permission
 * revocation (FR-012 — "location becomes unavailable after the map view is active") is
 * observed and reflected, not just the initial resolution. */
export function useGeolocation(): GeolocationState {
  // Lazy initializer decides the unsupported case up front, so the effect never needs to call
  // setState synchronously on mount just to report "unavailable" (react-hooks/set-state-in-effect).
  const [state, setState] = useState<GeolocationState>(() =>
    typeof navigator === 'undefined' || !navigator.geolocation
      ? { status: 'unavailable', latitude: null, longitude: null }
      : { status: 'resolving', latitude: null, longitude: null },
  )

  useEffect(() => {
    if (typeof navigator === 'undefined' || !navigator.geolocation) return

    const watchId = navigator.geolocation.watchPosition(
      (position) => {
        setState({
          status: 'granted',
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
        })
      },
      () => {
        setState({ status: 'unavailable', latitude: null, longitude: null })
      },
      { enableHighAccuracy: false, timeout: GEOLOCATION_TIMEOUT_MS },
    )

    return () => navigator.geolocation.clearWatch(watchId)
  }, [])

  return state
}
