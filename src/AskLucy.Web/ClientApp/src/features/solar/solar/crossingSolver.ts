/**
 * The instant at which a quantity of the sun's position reaches a target — Newton iteration on
 * the instant, seeded from the closed form (specs/052 research D3; extracted from `daySummary.ts`
 * by specs/085 research D4 so the twilight thresholds reuse it rather than duplicating its
 * convergence guarantees).
 *
 * Two properties are load-bearing and are the reason this is shared rather than copied:
 *
 * - **It iterates on whole milliseconds**, so the instant that was tested is exactly the instant
 *   returned. No rounding happens after the last check.
 * - **It returns `null` rather than the seed** when it does not converge. A seed is accurate to
 *   minutes; returning it would present an approximation as a solved answer, which is the one
 *   outcome both callers' contracts forbid.
 *
 * Pure functions of their arguments — no rendering, DOM, network or module state (constitution
 * §2.V).
 */

/**
 * The solver's evaluation cap. Newton roughly doubles the number of correct digits per step and
 * the closed-form seed already lands within about three minutes of the answer, so convergence is
 * reached in five or so. Twelve is a bound, not a working figure.
 */
export const CROSSING_SOLVER_MAX_ITERATIONS = 12

/**
 * The solver's convergence threshold, in degrees of the quantity being driven to zero.
 *
 * specs/052 research D3 proposed 1e-7°, which is not attainable: `Date` resolves to whole
 * milliseconds, and at the equator the sun climbs about 4.2e-6° per millisecond, so the altitude
 * the function can be evaluated at is quantised more coarsely than 1e-7. 1e-5° sits comfortably
 * above that quantum everywhere — it is always reachable at the nearest millisecond — and
 * corresponds to under three milliseconds of time error, some four orders of magnitude inside
 * the rise/set tolerance.
 */
export const CROSSING_SOLVER_CONVERGENCE_DEGREES = 1e-5

/** The half-interval of the central finite difference used for Newton's slope. */
const SLOPE_STEP_MS = 30_000

/**
 * Solves for the instant at which `evaluate` reaches zero, starting from `seedMs`.
 *
 * `evaluate` returns the signed distance from the target, in degrees, at the given instant — the
 * caller decides which quantity that is (the sun's upper edge on the horizon for rise and set; the
 * sun's centre at a twilight depth for the thresholds).
 *
 * Returns `null` on non-convergence. It deliberately does not fall back to the seed: see this
 * module's own note above.
 */
export function solveCrossingMs(
  seedMs: number,
  evaluate: (instantMs: number) => number,
  maxIterations: number = CROSSING_SOLVER_MAX_ITERATIONS,
): number | null {
  let instantMs = Math.round(seedMs)

  for (let iteration = 0; iteration < maxIterations; iteration += 1) {
    const value = evaluate(instantMs)
    if (Math.abs(value) <= CROSSING_SOLVER_CONVERGENCE_DEGREES) return instantMs

    const slopeDegreesPerMs =
      (evaluate(instantMs + SLOPE_STEP_MS) - evaluate(instantMs - SLOPE_STEP_MS)) / (2 * SLOPE_STEP_MS)

    // A vanishing slope is the sun grazing the target — Newton has nothing to descend, and the
    // answer is genuinely undetermined rather than merely slow to find.
    if (!Number.isFinite(slopeDegreesPerMs) || slopeDegreesPerMs === 0) return null

    const nextMs = Math.round(instantMs - value / slopeDegreesPerMs)
    if (!Number.isFinite(nextMs)) return null
    instantMs = nextMs
  }

  return null
}

/**
 * The cosine of the hour angle at which the sun's centre reaches `targetAltitudeDegrees` on a
 * date with the given declination, for a site at the given latitude — the closed form the seed
 * and the polar test are both built on.
 *
 * A value outside [−1, 1] means the sun does not reach that altitude on that date at all:
 * above 1 it stays below it all day, below −1 it stays above it all day. Callers use that to
 * distinguish "never happens" from "happens twice", which is the whole of the polar handling
 * (specs/052 FR-004; specs/085 research D6).
 */
export function cosHourAngle(
  latitudeDegrees: number,
  declinationDegrees: number,
  targetAltitudeDegrees: number,
): number {
  const toRad = (degrees: number): number => (degrees * Math.PI) / 180
  return (
    (Math.sin(toRad(targetAltitudeDegrees)) -
      Math.sin(toRad(latitudeDegrees)) * Math.sin(toRad(declinationDegrees))) /
    (Math.cos(toRad(latitudeDegrees)) * Math.cos(toRad(declinationDegrees)))
  )
}

/**
 * The seed instant for a crossing, from the closed form: solar noon, displaced by the hour angle.
 * The morning crossing falls before solar noon and the evening one after it.
 */
export function crossingSeedMs(
  dayStartUtcMs: number,
  longitudeDegrees: number,
  eqTimeMinutes: number,
  hourAngleDegrees: number,
  direction: 'morning' | 'evening',
): number {
  const solarNoonMinutes = 720 - 4 * longitudeDegrees - eqTimeMinutes
  const offsetMinutes = hourAngleDegrees * 4 * (direction === 'morning' ? -1 : 1)
  return dayStartUtcMs + (solarNoonMinutes + offsetMinutes) * 60_000
}
