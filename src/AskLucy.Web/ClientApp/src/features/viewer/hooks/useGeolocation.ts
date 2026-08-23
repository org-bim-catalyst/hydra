import { useEffect, useState } from 'react'

export type GeolocationStatus = 'resolving' | 'granted' | 'unavailable'

export interface GeolocationState {
  status: GeolocationStatus
  latitude: number | null
  longitude: number | null
}

/** FR-005: total timeout before the app falls back to the neutral state. Matches the
 * geocoding search timeout in spec 035. */
const GEOLOCATION_TIMEOUT_MS = 15_000

/** FR-013: inner window for the high-accuracy attempt. If GPS-grade precision is not
 * available within 3 s, the low-accuracy watchPosition result is used instead. */
const HIGH_ACCURACY_TIMEOUT_MS = 3_000

/** FR-001/FR-013: requests the user's current location via the browser Geolocation API.
 *
 * Two-phase strategy (research.md Decision 4):
 * 1. `getCurrentPosition` with high accuracy and a 3 s inner timeout — commits immediately
 *    when GPS is readily available (< 1 s on clear-sky mobile), keeping well inside SC-001's
 *    5-second target.
 * 2. `watchPosition` with low accuracy runs concurrently for mid-session revocation detection
 *    (FR-012) and as the primary source when high accuracy is unavailable.
 *
 * Denied, unsupported, and timed-out are all treated identically as `'unavailable'` (spec.md
 * Edge Cases) — never surfaced as an error to the user (FR-004). */
export function useGeolocation(): GeolocationState {
  const [state, setState] = useState<GeolocationState>(() =>
    typeof navigator === 'undefined' || !navigator.geolocation
      ? { status: 'unavailable', latitude: null, longitude: null }
      : { status: 'resolving', latitude: null, longitude: null },
  )

  useEffect(() => {
    if (typeof navigator === 'undefined' || !navigator.geolocation) return

    // FR-013: attempt high-accuracy GPS first. On success, immediately commit — the low-accuracy
    // watchPosition below will continue to run for revocation detection and may produce a
    // subsequent update with slightly different city-level coordinates, which is acceptable.
    // On failure (timeout or any error), silently skip — watchPosition provides the result.
    navigator.geolocation.getCurrentPosition(
      (position) => {
        setState({
          status: 'granted',
          latitude: position.coords.latitude,
          longitude: position.coords.longitude,
        })
      },
      () => {
        // Intentionally empty: low-accuracy watchPosition is already running as the fallback.
      },
      { enableHighAccuracy: true, timeout: HIGH_ACCURACY_TIMEOUT_MS },
    )

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
