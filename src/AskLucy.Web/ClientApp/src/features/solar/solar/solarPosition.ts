/**
 * research D1 — the NOAA solar-position algorithm (public-domain formulas), ported from the
 * reference implementation (`sunpath-osm-shadows-13.html`) rather than adopted as a runtime
 * dependency. Pure functions of `(instantUtc, latitude, longitude)` — no rendering imports, so
 * this module is unit-testable with zero GPU/DOM involvement (constitution §2.V).
 *
 * Tolerances are exported as named constants (T006) so the accuracy shown to the user in the
 * figures panel (contracts/solar-panels.md, FR-044) is exactly the number the tests in
 * `solarPosition.test.ts` assert against published NOAA Solar Calculator reference values —
 * the two cannot drift apart because there is only one source for the figure.
 */

/** research D1 — measured (not inherited from NOAA, which states no position figure) against
 * published NOAA Solar Calculator values at the SC-001 test locations. */
export const SOLAR_POSITION_TOLERANCE_DEGREES = 0.1

/** research D1 — inherited from NOAA's own documented accuracy for latitudes within ±72°. */
export const RISE_SET_TOLERANCE_SECONDS = 60

/** research D1 — inherited from NOAA's own documented accuracy for latitudes beyond ±72°. */
export const RISE_SET_TOLERANCE_SECONDS_HIGH_LATITUDE = 600

/** research D1 — the latitude band inside which the tighter rise/set tolerance applies. */
export const HIGH_LATITUDE_THRESHOLD_DEGREES = 72

export interface SolarPositionResult {
  /** Compass direction, clockwise from true north, 0…360. */
  azimuthDegrees: number
  /** Height above the horizon, −90…90. Negative means below the horizon. */
  altitudeDegrees: number
  /** Apparent solar declination for the instant, in degrees — an intermediate NOAA quantity
   * `daySummary.ts` reuses rather than recomputing. */
  declination: number
  /** The NOAA equation-of-time correction for the instant, in minutes — likewise reused by
   * `daySummary.ts` for solar noon. */
  eqTime: number
}

const toRad = (degrees: number): number => (degrees * Math.PI) / 180
const toDeg = (radians: number): number => (radians * 180) / Math.PI
const norm360 = (value: number): number => {
  const wrapped = value % 360
  return wrapped < 0 ? wrapped + 360 : wrapped
}

function julianDay(date: Date): number {
  return date.getTime() / 86_400_000 + 2_440_587.5
}

/**
 * NOAA solar-position algorithm — mean longitude, equation of centre, apparent longitude,
 * obliquity correction, declination, equation of time, hour angle → altitude/azimuth (FR-001).
 * Ported verbatim from the reference implementation's exercised, working formulas.
 */
export function solarPosition(instantUtc: Date, latitude: number, longitude: number): SolarPositionResult {
  const julianDate = julianDay(instantUtc)
  const T = (julianDate - 2_451_545.0) / 36_525.0
  const L0 = norm360(280.46646 + T * (36000.76983 + T * 0.0003032))
  const M = 357.52911 + T * (35999.05029 - 0.0001537 * T)
  const e = 0.016708634 - T * (0.000042037 + 0.0000001267 * T)
  const Mrad = toRad(M)
  const C =
    Math.sin(Mrad) * (1.914602 - T * (0.004817 + 0.000014 * T)) +
    Math.sin(2 * Mrad) * (0.019993 - 0.000101 * T) +
    Math.sin(3 * Mrad) * 0.000289
  const trueLong = L0 + C
  const omega = 125.04 - 1934.136 * T
  const appLong = trueLong - 0.00569 - 0.00478 * Math.sin(toRad(omega))
  const meanObliq = 23 + (26 + (21.448 - T * (46.815 + T * (0.00059 - T * 0.001813))) / 60) / 60
  const obliqCorr = meanObliq + 0.00256 * Math.cos(toRad(omega))
  const declRad = Math.asin(Math.sin(toRad(obliqCorr)) * Math.sin(toRad(appLong)))
  const declination = toDeg(declRad)
  const y = Math.tan(toRad(obliqCorr / 2)) ** 2
  const eqTime = toDeg(
    y * Math.sin(2 * toRad(L0)) -
      2 * e * Math.sin(Mrad) +
      4 * e * y * Math.sin(Mrad) * Math.cos(2 * toRad(L0)) -
      0.5 * y * y * Math.sin(4 * toRad(L0)) -
      1.25 * e * e * Math.sin(2 * Mrad),
  ) * 4

  const utcMinutes = instantUtc.getUTCHours() * 60 + instantUtc.getUTCMinutes() + instantUtc.getUTCSeconds() / 60
  let trueSolarTime = (utcMinutes + eqTime + 4 * longitude) % 1440
  if (trueSolarTime < 0) trueSolarTime += 1440
  const hourAngle = trueSolarTime / 4 - 180

  const latRad = toRad(latitude)
  const zenithRad = Math.acos(
    Math.sin(latRad) * Math.sin(declRad) + Math.cos(latRad) * Math.cos(declRad) * Math.cos(toRad(hourAngle)),
  )
  const altitudeDegrees = 90 - toDeg(zenithRad)
  const cosAz =
    (Math.sin(latRad) * Math.cos(zenithRad) - Math.sin(declRad)) / (Math.cos(latRad) * Math.sin(zenithRad))
  const azAcos = toDeg(Math.acos(Math.min(1, Math.max(-1, cosAz))))
  const azimuthDegrees = hourAngle > 0 ? (azAcos + 180) % 360 : (540 - azAcos) % 360

  return { azimuthDegrees, altitudeDegrees, declination, eqTime }
}

/** data-model.md "Solar Position" — the ENU unit vector used to place the shadow-casting light
 * and the sun-path dome geometry. Directly usable as a Three.js position because specs/051's
 * local frame (X=East, Y=North, Z=Up) maps onto the scene axes with no remapping. */
export function solarPositionToEnuUnitVector(azimuthDegrees: number, altitudeDegrees: number): { x: number; y: number; z: number } {
  const az = toRad(azimuthDegrees)
  const alt = toRad(altitudeDegrees)
  return {
    x: Math.cos(alt) * Math.sin(az),
    y: Math.cos(alt) * Math.cos(az),
    z: Math.sin(alt),
  }
}
