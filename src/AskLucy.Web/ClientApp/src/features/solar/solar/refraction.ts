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
 * where the geometric altitude of a sun whose upper edge is on the horizon sits near −0.84°.
 *
 * **Below −0.575° the fit is not used.** research D2 lists a fourth expression, `−20.772/tan(te)`,
 * for that range, and taking it literally is wrong here. It *decreases* as the altitude falls —
 * 0.5749° at −0.575°, 0.4569° at −0.724°, 0.3968° at −0.833°, reaching zero at the nadir — whereas
 * refraction physically keeps increasing as the ray grazes lower through more atmosphere. That
 * expression is not a sub-horizon refraction model; NOAA's own rise/set constant contradicts it,
 * using 34′ (0.5667°) of horizon refraction at −0.833° where the expression yields 0.3968°.
 *
 * This mattered, and was caught by measurement rather than by reading. Evaluated inside that band,
 * the upper-edge rise/set solve lands at a geometric −0.7236° instead of the conventional −0.833°,
 * and that 0.11° offset showed up as a systematic error against NOAA's published tables that grows
 * as the sun's ascent flattens: every rise late and every set early, by 26–60 s at Dubai, London
 * and Singapore, 82–99 s at Tromsø, and up to 200 s at Reykjavík — outside the tolerance FR-004a
 * requires, at latitudes well inside the ±72° band.
 *
 * So the fit is held at its boundary value outside the range it was fitted for, which is the
 * ordinary treatment of an extrapolation with no physical validity. Horizon refraction then
 * becomes a constant 0.5749° (34.5′), agreeing with the Astronomical Almanac's standard 34′ and
 * with the −0.833° convention NOAA's published times are built on, while the rise/set altitude
 * this feature reports stays exactly −`SOLAR_SEMIDIAMETER_DEGREES`. Both of the spec's criteria
 * hold at once; evaluating the fourth band satisfies only the first.
 *
 * Continuity: the band boundaries do not join exactly, because NOAA's expressions are independent
 * fits rather than a spline. The largest step is 0.0014° at 85° and 0.0005° at 5° — both far
 * inside `SOLAR_POSITION_TOLERANCE_DEGREES`, which is what the contract requires and what
 * `refraction.test.ts` pins. The −0.575° boundary now joins exactly, since holding the boundary
 * value is continuous by construction.
 */
/** The lowest true elevation NOAA's near-horizon polynomial was fitted for. */
const NEAR_HORIZON_FIT_FLOOR_DEGREES = -0.575

function nearHorizonFitDegrees(te: number): number {
  return (1735 + te * (-518.2 + te * (103.4 + te * (-12.79 + 0.711 * te)))) / ARCSECONDS_PER_DEGREE
}

/**
 * Refraction at and below the horizon, held constant — see the note above. 0.5749°, or 34.5′,
 * which is the standard horizon refraction the −0.833° rise/set convention is built on.
 */
export const HORIZON_REFRACTION_DEGREES = nearHorizonFitDegrees(NEAR_HORIZON_FIT_FLOOR_DEGREES)

export function refractionCorrectionDegrees(geometricAltitudeDegrees: number): number {
  if (geometricAltitudeDegrees > 85) return 0

  if (geometricAltitudeDegrees > 5) {
    const tangent = Math.tan(toRad(geometricAltitudeDegrees))
    return (58.1 / tangent - 0.07 / tangent ** 3 + 0.000086 / tangent ** 5) / ARCSECONDS_PER_DEGREE
  }

  if (geometricAltitudeDegrees > NEAR_HORIZON_FIT_FLOOR_DEGREES) {
    return nearHorizonFitDegrees(geometricAltitudeDegrees)
  }

  return HORIZON_REFRACTION_DEGREES
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
