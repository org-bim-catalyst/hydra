import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import * as weatherApi from '../api/weatherApi'

/** FR-011: a reading older than this is shown with a staleness indicator rather than treated
 * as current. Also used as staleTime so a location change (new query key) always fetches fresh,
 * but a same-location re-mount within this window uses the cache (specs/036 T019 — no timer).
 */
const STALE_AFTER_MS = 30 * 60 * 1000

/** FR-009/FR-011: fetches the current weather for a resolved location. Refresh is driven
 * exclusively by query-key changes (latitude/longitude change → new key → automatic refetch)
 * per specs/036-startup-geolocation T019 — no periodic timer, which avoids sending coordinates
 * to the backend on a schedule rather than on user intent (FR-011). A coordinate change from
 * either startup geolocation or an agent-confirmed location is already enough of a signal.
 * Disabled (no request) while latitude/longitude are null (FR-008). */
export function useCurrentWeather(latitude: number | null, longitude: number | null) {
  const query = useQuery({
    queryKey: ['weather', 'current', latitude, longitude],
    queryFn: () => weatherApi.getCurrentWeather(latitude as number, longitude as number),
    enabled: latitude !== null && longitude !== null,
    staleTime: STALE_AFTER_MS,
    // FR-011: a failed background refresh must not blank out an already-showing reading —
    // keeps the last successful snapshot in `data` (surfaced as stale below) instead of
    // clearing it just because the latest refetch attempt failed.
    placeholderData: keepPreviousData,
  })

  // `Date.now()` is impure and can't be called during render (react-hooks/purity) — tracked as
  // state instead, ticking often enough that crossing the 30-minute staleness threshold is
  // reflected promptly without needing a fresh fetch.
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const intervalId = setInterval(() => setNow(Date.now()), 60_000)
    return () => clearInterval(intervalId)
  }, [])

  const isStale = query.data
    ? now - new Date(query.data.observedAtUtc).getTime() > STALE_AFTER_MS || query.isError
    : false

  return { ...query, isStale }
}
