import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import * as weatherApi from '../api/weatherApi'

/** FR-010: periodic refresh while the workspace stays open. */
const WEATHER_REFETCH_INTERVAL_MS = 15 * 60 * 1000

/** FR-011: a reading older than this is shown with a staleness indicator rather than treated
 * as current. */
const STALE_AFTER_MS = 30 * 60 * 1000

/** FR-009/FR-010/FR-011: fetches the current weather for a resolved location, refreshing
 * periodically. Disabled (no request issued) while `latitude`/`longitude` are `null` — matches
 * FR-008's "no map content or weather widget MUST be requested or shown" when location hasn't
 * resolved. */
export function useCurrentWeather(latitude: number | null, longitude: number | null) {
  const query = useQuery({
    queryKey: ['weather', 'current', latitude, longitude],
    queryFn: () => weatherApi.getCurrentWeather(latitude as number, longitude as number),
    enabled: latitude !== null && longitude !== null,
    refetchInterval: WEATHER_REFETCH_INTERVAL_MS,
    staleTime: WEATHER_REFETCH_INTERVAL_MS,
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
