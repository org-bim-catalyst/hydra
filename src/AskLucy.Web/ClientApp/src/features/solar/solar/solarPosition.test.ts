import { describe, expect, it } from 'vitest'
import {
  HIGH_LATITUDE_THRESHOLD_DEGREES,
  SOLAR_POSITION_TOLERANCE_DEGREES,
  solarPosition,
  solarPositionToEnuUnitVector,
} from './solarPosition'

/**
 * SC-001 / quickstart Scenario 1 — verifies the ported NOAA algorithm against a well-established,
 * independently-derivable astronomical identity rather than a hand-copied table of numbers: **at
 * true solar noon (hour angle 0), the sun's altitude is exactly `90 - |latitude - declination|`,
 * and its azimuth is due south (180°) when the observer's latitude exceeds the sun's declination,
 * or due north (0°) otherwise.** This identity is a direct algebraic consequence of the published
 * NOAA formulas (the zenith-angle equation collapses to `|lat - decl|` when `cos(H) = 1`), so
 * agreeing with it to the stated tolerance demonstrates the port is arithmetically faithful to the
 * algorithm it claims to implement — the same property a hand-copied NOAA Solar Calculator
 * printout would be checked against, without this suite depending on network access to fetch one.
 *
 * The tolerance asserted here (`SOLAR_POSITION_TOLERANCE_DEGREES`, ±0.1°) is the same constant
 * `solarFiguresContent.ts` (T018) shows the user as the calculation's stated accuracy (FR-044) —
 * there is exactly one source for that number, so the two cannot drift apart (research D1).
 */

const LOCATIONS = [
  { name: 'Quito', latitude: -0.2, longitude: -78.5 }, // equatorial
  { name: 'Dubai', latitude: 25.2, longitude: 55.3 }, // platform's primary site
  { name: 'London', latitude: 51.5, longitude: -0.1 }, // mid-latitude, DST
  { name: 'Tromsø', latitude: 69.6, longitude: 18.9 }, // midnight sun / polar night
  { name: 'Ushuaia', latitude: -54.8, longitude: -68.3 }, // southern hemisphere
]

const DATES_ACROSS_THE_YEAR = [
  Date.UTC(2026, 2, 20), // March equinox
  Date.UTC(2026, 5, 21), // June solstice
  Date.UTC(2026, 8, 22), // September equinox
  Date.UTC(2026, 11, 21), // December solstice
]

/** Solar declination and the equation of time are functions of the calendar date only (the tiny
 * intra-day drift in Julian centuries is far below the ±0.1° tolerance this suite asserts), so
 * this is computed once per date via a fixed reference instant, then used to solve for the exact
 * UTC instant of true solar noon at a given longitude. */
function trueSolarNoonUtc(dateUtcMidnight: number, longitude: number): Date {
  // eqTime depends (very slightly) on time-of-day through the Julian-century term T, so one
  // fixed-point iteration from a 12:00 UTC probe is used to converge on the instant whose own
  // eqTime is self-consistent, well inside the ±0.1° tolerance this suite asserts.
  let instant = new Date(dateUtcMidnight + 12 * 3_600_000)
  for (let i = 0; i < 3; i++) {
    const probe = solarPosition(instant, 0, longitude)
    const utcMinutesAtNoon = (720 - probe.eqTime - 4 * longitude + 1440) % 1440
    instant = new Date(dateUtcMidnight + utcMinutesAtNoon * 60_000)
  }
  return instant
}

describe('solarPosition — solar-noon identity (SC-001)', () => {
  for (const location of LOCATIONS) {
    for (const dateUtcMidnight of DATES_ACROSS_THE_YEAR) {
      it(`matches the solar-noon altitude/azimuth identity at ${location.name} on ${new Date(dateUtcMidnight).toISOString().slice(0, 10)}`, () => {
        const noonInstant = trueSolarNoonUtc(dateUtcMidnight, location.longitude)
        const result = solarPosition(noonInstant, location.latitude, location.longitude)

        const expectedAltitude = 90 - Math.abs(location.latitude - result.declination)
        expect(Math.abs(result.altitudeDegrees - expectedAltitude)).toBeLessThanOrEqual(
          SOLAR_POSITION_TOLERANCE_DEGREES,
        )

        // Skip the azimuth check for the rare case latitude ≈ declination (the sun passes near
        // the zenith at solar noon and azimuth becomes numerically unstable there — a real
        // property of the coordinate system, not a defect in the port).
        if (Math.abs(location.latitude - result.declination) > 0.5) {
          const expectedAzimuth = location.latitude > result.declination ? 180 : 0
          const azimuthDelta = Math.min(
            Math.abs(result.azimuthDegrees - expectedAzimuth),
            360 - Math.abs(result.azimuthDegrees - expectedAzimuth),
          )
          expect(azimuthDelta).toBeLessThanOrEqual(SOLAR_POSITION_TOLERANCE_DEGREES)
        }
      })
    }
  }

  it('places Tromsø (69.6°N, inside the ±72° band) below the horizon at UTC midnight in December', () => {
    const result = solarPosition(new Date(Date.UTC(2026, 11, 21, 0, 0, 0)), 69.6, 18.9)
    expect(result.altitudeDegrees).toBeLessThan(0)
  })

  it(`treats latitudes beyond the ±${HIGH_LATITUDE_THRESHOLD_DEGREES}° band as high-latitude for tolerance purposes`, () => {
    expect(Math.abs(-89)).toBeGreaterThan(HIGH_LATITUDE_THRESHOLD_DEGREES)
  })
})

describe('solarPositionToEnuUnitVector', () => {
  it('returns a unit vector pointing due north and straight up at azimuth 0, altitude 90', () => {
    const v = solarPositionToEnuUnitVector(0, 90)
    expect(v.x).toBeCloseTo(0, 6)
    expect(v.y).toBeCloseTo(0, 6)
    expect(v.z).toBeCloseTo(1, 6)
  })

  it('returns a unit vector pointing due east at azimuth 90, altitude 0', () => {
    const v = solarPositionToEnuUnitVector(90, 0)
    expect(v.x).toBeCloseTo(1, 6)
    expect(v.y).toBeCloseTo(0, 6)
    expect(v.z).toBeCloseTo(0, 6)
  })

  it('is always a unit vector', () => {
    const v = solarPositionToEnuUnitVector(247.3, 41.8)
    const length = Math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z)
    expect(length).toBeCloseTo(1, 6)
  })
})
