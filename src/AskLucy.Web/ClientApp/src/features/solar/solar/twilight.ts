import { cosHourAngle, crossingSeedMs, solveCrossingMs } from './crossingSolver'
import { daySummary, type DaySummary } from './daySummary'
import { HORIZON_REFRACTION_DEGREES, SOLAR_SEMIDIAMETER_DEGREES } from './refraction'
import { solarPosition } from './solarPosition'
import { fromLocalParts, toLocalParts } from './timeZone'

/**
 * Twilight thresholds and the year's day-length profile (specs/085).
 *
 * The thresholds are the **geometric altitude of the sun's centre** at 6°, 12° and 18° below the
 * horizon — the published definition (NOAA, NWS, USNO), so a user checking a phase against an
 * almanac finds the same time (research D1). `solarPosition.ts` returns the *apparent* altitude,
 * and `refraction.ts` holds the correction at a constant `HORIZON_REFRACTION_DEGREES` for every
 * geometric altitude at or below −0.575°, which every threshold is. A crossing is therefore
 * solved as the instant the apparent altitude equals `−threshold + HORIZON_REFRACTION_DEGREES` —
 * exact, not an approximation, and cheap enough to use inside the Newton iteration (research D2).
 */

export type TwilightPhase = 'civil' | 'nautical' | 'astronomical'
export type TwilightDirection = 'ascending' | 'descending'
export type DayPhase = 'night' | 'astronomical' | 'nautical' | 'civil' | 'day'

export interface TwilightThreshold {
  phase: TwilightPhase
  /** The sun's centre, geometrically, below the horizon. */
  degreesBelowHorizon: number
}

/** The three published thresholds, in the order the panel reports them (increasing darkness). */
export const TWILIGHT_THRESHOLDS: readonly TwilightThreshold[] = [
  { phase: 'civil', degreesBelowHorizon: 6 },
  { phase: 'nautical', degreesBelowHorizon: 12 },
  { phase: 'astronomical', degreesBelowHorizon: 18 },
]

export interface TwilightCrossing {
  phase: TwilightPhase
  direction: TwilightDirection
  /** The solved instant, or `null` when the threshold is not reached or could not be solved. */
  instantUtc: Date | null
  /** `false` when the sun's centre never reaches this threshold on that date. */
  reached: boolean
  /** `true` when the threshold is reached but the solver did not converge. Never a time then. */
  undetermined: boolean
}

export interface TwilightPhaseLength {
  phase: TwilightPhase
  /** The band's duration on the evening side; `null` when either of its boundaries is missing. */
  minutes: number | null
  /** The boundary the band starts from — the horizon for civil, the threshold above otherwise. */
  from: 'horizon' | TwilightPhase
}

export interface DayTwilightSummary {
  crossings: TwilightCrossing[]
  lengths: TwilightPhaseLength[]
}

export interface TwilightOptions {
  /** Test seam for the non-convergence path, as `daySummary` has. */
  maxIterations?: number
}

export interface PhaseSegment {
  phase: DayPhase
  /** Local minutes from the start of the site's local day. */
  startMinute: number
  endMinute: number
}

export interface DayPhaseSequence {
  date: string
  segments: PhaseSegment[]
  daylightMinutes: number
}

export interface YearExtreme {
  date: string
  minutes: number
}

export interface YearProfile {
  year: number
  latitude: number
  longitude: number
  timeZoneId: string
  days: DayPhaseSequence[]
  longestDay: YearExtreme
  shortestDay: YearExtreme
  hemisphere: 'northern' | 'southern'
}

/** The apparent centre altitude at which each phase boundary sits. The day boundary is the sun's
 * upper edge on the horizon, which is the same definition `daySummary` solves rise and set for. */
const PHASE_BOUNDARY_APPARENT_ALTITUDES: readonly { phase: DayPhase; atOrAbove: number }[] = [
  { phase: 'day', atOrAbove: -SOLAR_SEMIDIAMETER_DEGREES },
  { phase: 'civil', atOrAbove: -6 + HORIZON_REFRACTION_DEGREES },
  { phase: 'nautical', atOrAbove: -12 + HORIZON_REFRACTION_DEGREES },
  { phase: 'astronomical', atOrAbove: -18 + HORIZON_REFRACTION_DEGREES },
]

function phaseAt(instantMs: number, latitude: number, longitude: number): DayPhase {
  const altitude = solarPosition(new Date(instantMs), latitude, longitude).altitudeDegrees
  for (const boundary of PHASE_BOUNDARY_APPARENT_ALTITUDES) {
    if (altitude >= boundary.atOrAbove) return boundary.phase
  }
  return 'night'
}

/** The apparent altitude that represents a geometric threshold — research D2. */
function apparentAltitudeForThreshold(threshold: TwilightThreshold): number {
  return -threshold.degreesBelowHorizon + HORIZON_REFRACTION_DEGREES
}

/** The date key `daySummary` and this module both take: the site's local date, as UTC midnight. */
function dateKey(dateUtc: Date): string {
  return dateUtc.toISOString().slice(0, 10)
}

/** The closed-form seed and the reach test, shared by every threshold on a date. */
function thresholdGeometry(dateUtc: Date, latitude: number, longitude: number) {
  const noonReference = new Date(
    Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate(), 12, 0, 0),
  )
  const { declination, eqTime } = solarPosition(noonReference, latitude, longitude)
  const dayStartUtc = Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate(), 0, 0, 0)
  return { declination, eqTime, dayStartUtc }
}

const PER_DAY_CACHE = new Map<string, DayTwilightSummary>()
const PER_DAY_CACHE_LIMIT = 64

const PER_YEAR_CACHE = new Map<string, YearProfile>()
const PER_YEAR_CACHE_LIMIT = 4

function remember<K, V>(cache: Map<K, V>, key: K, value: V, limit: number): V {
  cache.set(key, value)
  if (cache.size > limit) {
    const oldest = cache.keys().next().value
    if (oldest !== undefined) cache.delete(oldest)
  }
  return value
}

/**
 * The selected date's twilight crossings and phase lengths, for a site.
 *
 * `dateUtc` follows `daySummary`'s convention: the site's local date expressed as UTC midnight.
 * The result is cached per (site, date) — a playback tick changes the time of day but not the
 * date, and must not re-solve six crossings for every frame (research D9).
 */
export function twilightCrossings(
  dateUtc: Date,
  latitude: number,
  longitude: number,
  options: TwilightOptions = {},
): DayTwilightSummary {
  const useCache = options.maxIterations === undefined
  const key = `${dateKey(dateUtc)}|${latitude}|${longitude}`
  if (useCache) {
    const cached = PER_DAY_CACHE.get(key)
    if (cached) return cached
  }

  const { declination, eqTime, dayStartUtc } = thresholdGeometry(dateUtc, latitude, longitude)
  const crossings: TwilightCrossing[] = []

  for (const threshold of TWILIGHT_THRESHOLDS) {
    const geometricTarget = -threshold.degreesBelowHorizon
    const cosH0 = cosHourAngle(latitude, declination, geometricTarget)
    const reached = Math.abs(cosH0) <= 1
    const apparentTarget = apparentAltitudeForThreshold(threshold)
    const evaluate = (instantMs: number): number =>
      solarPosition(new Date(instantMs), latitude, longitude).altitudeDegrees - apparentTarget

    for (const direction of ['ascending', 'descending'] as const) {
      if (!reached) {
        crossings.push({ phase: threshold.phase, direction, instantUtc: null, reached: false, undetermined: false })
        continue
      }

      const hourAngle = (Math.acos(Math.min(1, Math.max(-1, cosH0))) * 180) / Math.PI
      const seed = crossingSeedMs(dayStartUtc, longitude, eqTime, hourAngle, direction === 'ascending' ? 'morning' : 'evening')
      const solved = solveCrossingMs(seed, evaluate, options.maxIterations ?? undefined)

      crossings.push({
        phase: threshold.phase,
        direction,
        instantUtc: solved === null ? null : new Date(solved),
        reached: true,
        undetermined: solved === null,
      })
    }
  }

  const riseSet = daySummary(dateUtc, latitude, longitude)
  const lengths = phaseLengths(crossings, riseSet)
  const summary: DayTwilightSummary = { crossings, lengths }
  return useCache ? remember(PER_DAY_CACHE, key, summary, PER_DAY_CACHE_LIMIT) : summary
}

/** Research D5 — a phase's length is the band it occupies, on the evening (descending) side. */
function phaseLengths(crossings: TwilightCrossing[], riseSet: DaySummary): TwilightPhaseLength[] {
  const descending = (phase: TwilightPhase): number | null =>
    crossings.find((c) => c.phase === phase && c.direction === 'descending')?.instantUtc?.getTime() ?? null

  const sunset = riseSet.sunsetUtc?.getTime() ?? null
  const civil = descending('civil')
  const nautical = descending('nautical')
  const astronomical = descending('astronomical')

  const band = (from: number | null, to: number | null): number | null =>
    from === null || to === null ? null : (to - from) / 60_000

  return [
    { phase: 'civil', minutes: band(sunset, civil), from: 'horizon' },
    { phase: 'nautical', minutes: band(civil, nautical), from: 'civil' },
    { phase: 'astronomical', minutes: band(nautical, astronomical), from: 'nautical' },
  ]
}

/**
 * Every day of `year` at the site, with each day's phases in order — the chart's data
 * (research D9, D12). Computed once per (site, year) and cached; a time-of-day change never
 * rebuilds it, and neither does a date change within the same year.
 */
export function yearProfile(
  latitude: number,
  longitude: number,
  year: number,
  timeZoneId: string,
): YearProfile {
  const key = `${latitude}|${longitude}|${year}|${timeZoneId}`
  const cached = PER_YEAR_CACHE.get(key)
  if (cached) return cached

  const days: DayPhaseSequence[] = []
  const dayCount = isLeapYear(year) ? 366 : 365

  for (let index = 0; index < dayCount; index += 1) {
    days.push(dayPhaseSequence(latitude, longitude, year, index, timeZoneId))
  }

  const byMinutes = [...days].sort((a, b) => a.daylightMinutes - b.daylightMinutes)
  const shortest = byMinutes[0]
  const longest = byMinutes[byMinutes.length - 1]

  return remember(
    PER_YEAR_CACHE,
    key,
    {
      year,
      latitude,
      longitude,
      timeZoneId,
      days,
      longestDay: { date: longest.date, minutes: longest.daylightMinutes },
      shortestDay: { date: shortest.date, minutes: shortest.daylightMinutes },
      hemisphere: latitude >= 0 ? 'northern' : 'southern',
    },
    PER_YEAR_CACHE_LIMIT,
  )
}

export function isLeapYear(year: number): boolean {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0
}

/** The local calendar date of day `index` (0-based) in `year`. */
function localDateOf(year: number, index: number): string {
  return new Date(Date.UTC(year, 0, 1 + index)).toISOString().slice(0, 10)
}

function nextDate(localDate: string): string {
  const [year, month, day] = localDate.split('-').map(Number)
  return new Date(Date.UTC(year, month - 1, day + 1)).toISOString().slice(0, 10)
}

/**
 * One local day's phase sequence. The boundaries are every crossing that falls inside the local
 * day; between consecutive boundaries the phase is whatever the sun's altitude says, which is
 * what makes this agree with the figures by construction (research D12) rather than by a second
 * implementation.
 *
 * Segments are measured in local minutes from the local day's start, so a daylight-saving day is
 * 23 or 25 hours of minutes long and its phases still cover it contiguously (spec FR-017 as
 * amended).
 */
function dayPhaseSequence(
  latitude: number,
  longitude: number,
  year: number,
  index: number,
  timeZoneId: string,
): DayPhaseSequence {
  const date = localDateOf(year, index)
  const dayStart = fromLocalParts(date, 0, timeZoneId).getTime()
  const dayEnd = fromLocalParts(nextDate(date), 0, timeZoneId).getTime()

  const [y, m, d] = date.split('-').map(Number)
  const dateUtc = new Date(Date.UTC(y, m - 1, d))
  const summary = twilightCrossings(dateUtc, latitude, longitude)
  const riseSet = daySummary(dateUtc, latitude, longitude)

  const boundaries: number[] = []
  const consider = (instant: Date | null): void => {
    if (!instant) return
    const ms = instant.getTime()
    if (ms > dayStart && ms < dayEnd) boundaries.push(ms)
  }

  consider(riseSet.sunriseUtc)
  consider(riseSet.sunsetUtc)
  for (const cross of summary.crossings) consider(cross.instantUtc)
  boundaries.sort((a, b) => a - b)

  const edges = [dayStart, ...boundaries, dayEnd]
  const segments: PhaseSegment[] = []
  const totalMinutes = (dayEnd - dayStart) / 60_000

  for (let i = 0; i < edges.length - 1; i += 1) {
    const from = edges[i]
    const to = edges[i + 1]
    const phase = phaseAt((from + to) / 2, latitude, longitude)
    const startMinute = i === 0 ? 0 : Math.round((from - dayStart) / 60_000)
    const endMinute = i === edges.length - 2 ? Math.round(totalMinutes) : Math.round((to - dayStart) / 60_000)

    const previous = segments[segments.length - 1]
    if (previous && previous.phase === phase) previous.endMinute = endMinute
    else segments.push({ phase, startMinute, endMinute })
  }

  const daylightMinutes = segments
    .filter((segment) => segment.phase === 'day')
    .reduce((total, segment) => total + (segment.endMinute - segment.startMinute), 0)

  return { date, segments, daylightMinutes }
}

/** Exposed for callers that already hold an instant and need its local minute — the panel's
 * selected-date marker and the figures' display formatting share this conversion. */
export function localMinuteOfDay(instant: Date, timeZoneId: string): number {
  return toLocalParts(instant, timeZoneId).localMinuteOfDay
}
