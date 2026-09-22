/**
 * research D2 — the NOAA Solar Calculator's piecewise atmospheric refraction correction, taken
 * from the same public-domain source `solarPosition.ts` was ported from so this feature keeps a
 * single citable provenance and remains checkable against NOAA's published
 * "corrected for refraction" column.
 *
 * The defect this module repairs (research D1): the `−0.833°` rise/set threshold bundles two
 * unrelated physical effects — atmospheric refraction (~0.57° at the horizon) and the sun's
 * angular radius (0.267°). They belong in different places. Refraction is a property of the
 * *reported altitude*; the semidiameter is the *definition of rise and set*. Separating them is
 * what makes the reported altitude at sunrise the same number at every site and every date.
 *
 * Pure functions of their arguments — no rendering, DOM or network involvement (constitution §2 V).
 */

const toRad = (degrees: number): number => (degrees * Math.PI) / 180

const ARCSECONDS_PER_DEGREE = 3600

/**
 * The sun's mean angular radius, in degrees. Rise and set are defined as the instants its *upper
 * edge* touches the horizon, so at those instants the *centre* — the point every figure in this
 * feature reports — sits exactly this far below it. Exported rather than inlined because it is
 * simultaneously the definition used by `daySummary.ts` and the figure SC-001 asserts; the two
 * must not be able to disagree.
 *
 * **Not modelled**: the ±1.7% annual variation in apparent semidiameter with the Earth–Sun
 * distance (±0.0045°). That is an order of magnitude below `SOLAR_POSITION_TOLERANCE_DEGREES`, so
 * modelling it would change no displayed figure. Recorded here so the omission is a decision
 * rather than an oversight.
 */
export const SOLAR_SEMIDIAMETER_DEGREES = 0.2667

/**
 * research D2 — degrees to **add** to a geometric altitude to obtain the apparent (refracted) one.
 * Piecewise in the true elevation, evaluated in arcseconds exactly as NOAA states it.
 *
 * Pure: it never clamps to the caller's expectations and is defined for every finite input,
 * including altitudes below the horizon — `daySummary.ts`'s rise/set solver evaluates it there,
 * where the geometric altitude of a sun whose upper edge is on the horizon sits near −0.72°.
 *
 * Continuity: the band boundaries do not join exactly, because NOAA's four expressions are
 * independent fits rather than a spline. The largest step is 0.0014° at 85°, then 0.0005° at 5°
 * and 0.000001° at −0.575° — all far inside `SOLAR_POSITION_TOLERANCE_DEGREES`, which is what the
 * contract requires and what `refraction.test.ts` pins.
 */
export function refractionCorrectionDegrees(geometricAltitudeDegrees: number): number {
  if (geometricAltitudeDegrees > 85) return 0

  const tangent = Math.tan(toRad(geometricAltitudeDegrees))

  if (geometricAltitudeDegrees > 5) {
    return (58.1 / tangent - 0.07 / tangent ** 3 + 0.000086 / tangent ** 5) / ARCSECONDS_PER_DEGREE
  }

  if (geometricAltitudeDegrees > -0.575) {
    const te = geometricAltitudeDegrees
    return (1735 + te * (-518.2 + te * (103.4 + te * (-12.79 + 0.711 * te)))) / ARCSECONDS_PER_DEGREE
  }

  return -20.772 / tangent / ARCSECONDS_PER_DEGREE
}

/** Bisection steps used to invert the refraction correction. 60 halvings of a 1° bracket leave a
 * residual below 1e-18°, which is well under double precision — the loop is bounded for form's
 * sake, not because it is close to its limit. */
const INVERSION_STEPS = 60

/**
 * The geometric altitude whose apparent altitude is `apparentAltitudeDegrees` — the inverse of
 * `refractionCorrectionDegrees`, which has no closed form.
 *
 * Solved by bisection rather than by iterating `te ← apparent − refraction(te)`: that fixed-point
 * form has a slope approaching 1 just below the −0.575° band boundary, which is precisely the
 * region rise and set fall in. Bisection has no such failure mode.
 *
 * The bracket `[apparent − 1, apparent]` is always valid: refraction is never negative (so the
 * geometric altitude is never above the apparent one) and never exceeds 0.575° (so it is never
 * more than 1° below).
 */
export function geometricAltitudeForApparentDegrees(apparentAltitudeDegrees: number): number {
  let low = apparentAltitudeDegrees - 1
  let high = apparentAltitudeDegrees

  for (let step = 0; step < INVERSION_STEPS; step += 1) {
    const middle = (low + high) / 2
    if (middle + refractionCorrectionDegrees(middle) < apparentAltitudeDegrees) {
      low = middle
    } else {
      high = middle
    }
  }

  return (low + high) / 2
}

/**
 * FR-004 — the one horizon definition. The geometric altitude of the sun's centre at the instant
 * its refraction-corrected upper edge touches the horizon (≈ −0.724°), derived from the two
 * constants above rather than written down as a third number.
 *
 * `daySummary.ts` uses it for both the polar-condition test and the rise/set solver's seed, which
 * is how "the sun never rose" and "the sun rose at 06:41" are made incapable of disagreeing. It
 * replaces the bundled `−0.833°` the port inherited (research D1).
 */
export const RISE_SET_GEOMETRIC_ALTITUDE_DEGREES = geometricAltitudeForApparentDegrees(-SOLAR_SEMIDIAMETER_DEGREES)
