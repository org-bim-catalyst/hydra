import {
  CROSSING_SOLVER_MAX_ITERATIONS,
  cosHourAngle,
  crossingSeedMs,
  solveCrossingMs,
} from './crossingSolver'
import { RISE_SET_GEOMETRIC_ALTITUDE_DEGREES, SOLAR_SEMIDIAMETER_DEGREES } from './refraction'
import { solarPosition } from './solarPosition'

/** specs/085 research D4 — the solver moved to `crossingSolver.ts` so the twilight thresholds
 * reuse it. Re-exported under its original name because it is part of this module's published
 * surface and `daySummary.test.ts` asserts against it. */
export { CROSSING_SOLVER_CONVERGENCE_DEGREES as RISE_SET_SOLVER_CONVERGENCE_DEGREES } from './crossingSolver'

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

const toDeg = (radians: number): number => (radians * 180) / Math.PI


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

/** The quantity the rise/set solver drives to zero, bound to a site. */
function riseSetQuantity(latitude: number, longitude: number): (instantMs: number) => number {
  return (instantMs) => upperEdgeAltitudeDegrees(instantMs, latitude, longitude)
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
  const maxIterations = options.maxIterations ?? CROSSING_SOLVER_MAX_ITERATIONS

  const solarNoonReference = new Date(
    Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate(), 12, 0, 0),
  )
  const { declination, eqTime } = solarPosition(solarNoonReference, latitude, longitude)

  const cosH0 = cosHourAngle(latitude, declination, RISE_SET_GEOMETRIC_ALTITUDE_DEGREES)

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
  const dayStartUtc = Date.UTC(dateUtc.getUTCFullYear(), dateUtc.getUTCMonth(), dateUtc.getUTCDate(), 0, 0, 0)

  const evaluate = riseSetQuantity(latitude, longitude)
  const sunriseMs = solveCrossingMs(
    crossingSeedMs(dayStartUtc, longitude, eqTime, H0, 'morning'),
    evaluate,
    maxIterations,
  )
  const sunsetMs = solveCrossingMs(
    crossingSeedMs(dayStartUtc, longitude, eqTime, H0, 'evening'),
    evaluate,
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
