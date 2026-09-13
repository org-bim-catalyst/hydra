import { copy } from '../copy'

/** data-model.md "Site" — the site's local time basis. `timeZoneId` is `null` only when it could
 * not be determined; `timeBasisLabel` is always present and derived, never stored independently
 * (FR-003), so the stated basis and the basis actually used cannot drift apart. */
export interface TimeBasis {
  timeZoneId: string | null
  timeBasisLabel: string
}

/** research D2 — `tz-lookup` is a ~150 KB offline dataset; it is dynamically imported here so it
 * is lazy-loaded behind this feature (constitution §15 "large dependencies are lazy-loaded behind
 * the feature that needs them") rather than sitting in the initial bundle. */
async function lookupTimeZone(latitude: number, longitude: number): Promise<string | null> {
  try {
    const { default: tzLookup } = await import('tz-lookup')
    return tzLookup(latitude, longitude)
  } catch {
    // tz-lookup throws for coordinates it cannot place rather than returning null (verified
    // against the installed package), but the contract this module publishes to the rest of the
    // feature is "null when undetermined" either way — FR-003 requires the time basis be stated
    // "including when it cannot be determined", so this failure is never silent.
    return null
  }
}

/** research D2 — resolves the site's IANA time zone offline from its coordinates. FR-003: when it
 * cannot be determined, the time basis in use is stated explicitly rather than silently assumed —
 * `timeZoneId: null` with UTC named in `timeBasisLabel`, never a bare "UTC" that reads as if it
 * had been determined. */
export async function resolveTimeZone(latitude: number, longitude: number): Promise<TimeBasis> {
  const timeZoneId = await lookupTimeZone(latitude, longitude)
  if (!timeZoneId) {
    return { timeZoneId: null, timeBasisLabel: copy.timeBasisUndetermined }
  }
  return { timeZoneId, timeBasisLabel: timeZoneId }
}

/** The IANA zone id used whenever the site's time zone could not be determined (research D2). All
 * conversions still go through `Intl.DateTimeFormat`, exactly as they would for a resolved zone —
 * there is no separate "UTC path" to keep correct. */
export const FALLBACK_TIME_ZONE = 'UTC'

interface LocalDateParts {
  year: number
  month: number
  day: number
  hour: number
  minute: number
}

function formatParts(instantUtc: Date, timeZoneId: string): LocalDateParts {
  const formatter = new Intl.DateTimeFormat('en-US', {
    timeZone: timeZoneId,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
  const parts = formatter.formatToParts(instantUtc)
  const byType: Record<string, string> = {}
  for (const part of parts) byType[part.type] = part.value
  return {
    year: Number(byType.year),
    month: Number(byType.month),
    day: Number(byType.day),
    hour: Number(byType.hour),
    minute: Number(byType.minute),
  }
}

/** data-model.md "Analysis Moment" — projects the canonical `instantUtc` into the site's local
 * date and minute-of-day, via `Intl.DateTimeFormat` with the resolved (or fallback) IANA zone —
 * so daylight-saving transitions are handled by the browser's own tzdata rather than by any
 * arithmetic this feature would have to get right itself (FR-004). */
export function toLocalParts(instantUtc: Date, timeZoneId: string): { localDate: string; localMinuteOfDay: number } {
  const parts = formatParts(instantUtc, timeZoneId)
  const localDate = `${String(parts.year).padStart(4, '0')}-${String(parts.month).padStart(2, '0')}-${String(parts.day).padStart(2, '0')}`
  const localMinuteOfDay = parts.hour * 60 + parts.minute
  return { localDate, localMinuteOfDay }
}

/** The inverse of `toLocalParts` — data-model.md's "Rule": editing a local projection recomputes
 * `instantUtc`, never the reverse, which is what keeps FR-004 correct: a local time that occurs
 * twice (the autumn-back fold) or not at all (the spring-forward gap) resolves to exactly one UTC
 * instant, at one place, via fixed-point convergence against the zone's actual offset rather than
 * a cached or guessed one. */
export function fromLocalParts(localDate: string, localMinuteOfDay: number, timeZoneId: string): Date {
  const [year, month, day] = localDate.split('-').map(Number)
  const hour = Math.floor(localMinuteOfDay / 60)
  const minute = localMinuteOfDay % 60

  let guess = Date.UTC(year, month - 1, day, hour, minute, 0)
  for (let iteration = 0; iteration < 3; iteration++) {
    const observed = formatParts(new Date(guess), timeZoneId)
    const observedAsUtc = Date.UTC(observed.year, observed.month - 1, observed.day, observed.hour, observed.minute, 0)
    const driftMs = observedAsUtc - Date.UTC(year, month - 1, day, hour, minute, 0)
    if (driftMs === 0) break
    guess -= driftMs
  }
  return new Date(guess)
}
