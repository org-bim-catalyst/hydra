import { RISE_SET_GEOMETRIC_ALTITUDE_DEGREES, SOLAR_SEMIDIAMETER_DEGREES } from './refraction'
import { solarPosition } from './solarPosition'

/** data-model.md "Day Summary" — sunrise, sunset and day length for a site and date, including
 * the cases where the sun does not rise, does not set, or could not be determined (FR-002). */
export interface DaySummary {
  sunriseUtc: Date | null
  sunsetUtc: Date | null
  dayLengthMinutes: number | null
  polarCondition: 'none' | 'midnight-sun' | 'polar-night'
  /**
   * FR-028 / constitution §2 VIII — `true` when the rise/set solver did not converge, so the three
   * figures above are null because they are *unknown*, not because the sun never rose. Callers
   * must say so; returning the seed or the last iterate would be a plausible-looking wrong time,
   * which is the one outcome the contract forbids.
   *
   * Expected to be unreachable in practice given the seed's quality — but "unreachable" and
   * "unhandled" are different things.
   */
  riseSetUndetermined: boolean
}

const toRad = (degrees: number): number => (degrees * Math.PI) / 180
const toDeg = (radians: number): number => (radians * 180) / Math.PI

/**
 * research D3 — the solver's evaluation cap. Newton roughly doubles the number of correct digits
 * per step and the closed-form seed already lands within about three minutes of the answer, so
 * convergence is reached in five or so. Twelve is a bound, not a working figure.
 */
export const RISE_SET_SOLVER_MAX_ITERATIONS = 12

/**
 * research D3 — the solver's convergence threshold, in degrees of apparent altitude.
 *
 * research D3 proposed 1e-7°, which is not attainable: `Date` resolves to whole milliseconds, and
 * at the equator the sun climbs about 4.2e-6° per millisecond, so the altitude the function can be
 * evaluated at is quantised more coarsely than 1e-7. 1e-5° sits comfortably above that quantum
 * everywhere — it is always reachable at the nearest millisecond — and corresponds to under three
 * milliseconds of time error, some four orders of magnitude inside `RISE_SET_TOLERANCE_SECONDS`.
 */
export const RISE_SET_SOLVER_CONVERGENCE_DEGREES = 1e-5

/** research D3 — the half-interval of the central finite difference used for Newton's slope. */
const SLOPE_STEP_MS = 30_000

export interface DaySummaryOptions {
  /**
   * Test seam for FR-028's non-convergence path, which is otherwise unreachable. Setting this to
   * `0` denies the solver any evaluation at all, so it cannot reach the seed's answer and must
   * report the failure rather than return the seed. Defaults to `RISE_SET_SOLVER_MAX_ITERATIONS`.
   */
  maxIterations?: number
}

const UNDETERMINED: DaySummary = {
  sunriseUtc: null,
  sunsetUtc: null,
  dayLengthMinutes: null,
  polarCondition: 'none',
  riseSetUndetermined: true,
}

/**
 * FR-002a — the quantity being driven to zero: the apparent altitude of the sun's **upper edge**.
 * Zero means the upper edge is exactly on the horizon, which is the definition of rise and set,
 * and is why `solarPosition().altitudeDegrees` at the returned instants is always
 * `-SOLAR_SEMIDIAMETER_DEGREES` — the same number at every site on every date (SC-001).
 */
function upperEdgeAltitudeDegrees(instantMs: number, latitude: number, longitude: number): number {
  return solarPosition(new Date(instantMs), latitude, longitude).altitudeDegrees + SOLAR_SEMIDIAMETER_DEGREES
}

/**
 * research D3 — Newton iteration on the instant, seeded from the closed form. Iterating on whole
 * milliseconds means the instant that was tested is exactly the instant returned; no rounding
 * happens after the last check.
 *
 * Returns `null` on non-convergence. It deliberately does not fall back to the seed: a seed is
 * accurate to minutes, and returning it would present an approximation as a solved answer.
 */
function solveCrossingMs(
  seedMs: number,
  latitude: number,
  longitude: number,
  maxIterations: number,
): number | null {
  let instantMs = Math.round(seedMs)

  for (let iteration = 0; iteration < maxIterations; iteration += 1) {
    const value = upperEdgeAltitudeDegrees(instantMs, latitude, longitude)
    if (Math.abs(value) <= RISE_SET_SOLVER_CONVERGENCE_DEGREES) return instantMs

    const slopeDegreesPerMs =
      (upperEdgeAltitudeDegrees(instantMs + SLOPE_STEP_MS, latitude, longitude) -
        upperEdgeAltitudeDegrees(instantMs - SLOPE_STEP_MS, latitude, longitude)) /
      (2 * SLOPE_STEP_MS)

    // A vanishing slope is the sun grazing the horizon — Newton has nothing to descend, and the
    // answer is genuinely undetermined rather than merely slow to find.
    if (!Number.isFinite(slopeDegreesPerMs) || slopeDegreesPerMs === 0) return null

    const nextMs = Math.round(instantMs - value / slopeDegreesPerMs)
    if (!Number.isFinite(nextMs)) return null
    instantMs = nextMs
  }

  return null
}

/**
 * data-model.md "Day Summary". The declination and equation-of-time NOAA needs for the closed-form
 * *seed* are taken at solar noon, as the reference implementation does; the answer itself is then
 * solved for at the instant in question (FR-002a, research D3), which is what removes the residual
 * that made the reported altitude at sunrise vary from −0.77° to −0.96° between sites.
 *
 * `polarCondition` is derived from the same `cosH0` bound the reference implementation uses to
 * distinguish "never rises" from "never sets", and is evaluated **before** any iteration is
 * attempted (FR-004) — but now against `RISE_SET_GEOMETRIC_ALTITUDE_DEGREES`, the single
 * upper-edge definition rise and set are solved for, rather than the bundled −0.833° the port
 * inherited. That is what makes it impossible for the panel to say both "the sun does not rise"
 * and "sunrise 06:41".
 *
 * @param dateUtc Any instant on the calendar date (UTC) to summarize — only its UTC
 * year/month/day are read. Callers pass the site's local date translated to a UTC instant via
 * `timeZone.ts`, so a date near a time-zone boundary summarizes the day the site actually means.
 */
export function daySummary(
  dateUtc: Date,
  latitude: number,
  longitude: number,
  options: DaySummaryOptions = {},
): DaySummary {
  const maxIterations = options.maxIterations ?? RISE_SET_SOLVER_MAX_ITERATIONS

  const solarNoonReference = new Date(
    Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate(), 12, 0, 0),
  )
  const { declination, eqTime } = solarPosition(solarNoonReference, latitude, longitude)

  const latRad = toRad(latitude)
  const declRad = toRad(declination)
  const cosH0 =
    (Math.sin(toRad(RISE_SET_GEOMETRIC_ALTITUDE_DEGREES)) - Math.sin(latRad) * Math.sin(declRad)) /
    (Math.cos(latRad) * Math.cos(declRad))

  // FR-004 — both polar branches are decided here, before a single iteration is attempted.
  if (cosH0 > 1) {
    // The sun never rises above the horizon on this date at this latitude.
    return {
      sunriseUtc: null,
      sunsetUtc: null,
      dayLengthMinutes: null,
      polarCondition: 'polar-night',
      riseSetUndetermined: false,
    }
  }
  if (cosH0 < -1) {
    // The sun never sets on this date at this latitude.
    return {
      sunriseUtc: null,
      sunsetUtc: null,
      dayLengthMinutes: null,
      polarCondition: 'midnight-sun',
      riseSetUndetermined: false,
    }
  }

  const H0 = toDeg(Math.acos(cosH0))
  const solarNoonMinutes = 720 - 4 * longitude - eqTime
  const dayStartUtc = Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate(), 0, 0, 0)

  const sunriseMs = solveCrossingMs(
    dayStartUtc + (solarNoonMinutes - H0 * 4) * 60_000,
    latitude,
    longitude,
    maxIterations,
  )
  const sunsetMs = solveCrossingMs(
    dayStartUtc + (solarNoonMinutes + H0 * 4) * 60_000,
    latitude,
    longitude,
    maxIterations,
  )

  if (sunriseMs === null || sunsetMs === null) return UNDETERMINED

  return {
    sunriseUtc: new Date(sunriseMs),
    sunsetUtc: new Date(sunsetMs),
    dayLengthMinutes: (sunsetMs - sunriseMs) / 60_000,
    polarCondition: 'none',
    riseSetUndetermined: false,
  }
}
