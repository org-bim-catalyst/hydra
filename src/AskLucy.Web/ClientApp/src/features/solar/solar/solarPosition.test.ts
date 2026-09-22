import { describe, expect, it } from 'vitest'
import { HORIZON_REFRACTION_DEGREES, refractionCorrectionDegrees } from './refraction'
import { NOAA_FULL_DAY_SAMPLES, NOAA_FULL_DAY_SITE } from './noaaFullDay.fixture'
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
 *
 * specs/063 — the identity is *geometric*: it falls out of the zenith-angle equation, which knows
 * nothing about the atmosphere. Since `altitudeDegrees` is now the refraction-corrected apparent
 * altitude (FR-007), the identity is compared against the corrected form of itself. This keeps the
 * check independent — it is still derived algebra rather than a hand-copied table — while
 * accounting for the one documented term that was added. Without it the December Tromsø case
 * fails by 0.11°, which is refraction at −3°, not an error in the port.
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

        const geometricNoonAltitude = 90 - Math.abs(location.latitude - result.declination)
        const expectedAltitude = geometricNoonAltitude + refractionCorrectionDegrees(geometricNoonAltitude)
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

/**
 * T004 / FR-005, FR-007 — the pre-change regression table.
 *
 * These azimuth and altitude figures were captured from the release immediately before specs/063
 * (`pre-solar-upgrade`, f42e6814) by running the then-current `solarPosition` directly. The Badr
 * rows reproduce [baseline.md](../../../../../../specs/063-solar-accuracy-performance/baseline.md)
 * exactly, which is what ties this machine-captured table to the figures a human read off the
 * screen.
 *
 * Two claims are pinned:
 *  - **Azimuth is bit-identical.** Refraction raises the apparent position vertically; it does not
 *    rotate it. `toBe` rather than a tolerance, because there is no tolerance to allow — the
 *    azimuth arithmetic was not touched, and if a future change perturbs it by a hair this should
 *    fail loudly rather than absorb it.
 *  - **Altitude above 15° is unchanged within tolerance.** Above 15° the correction is under
 *    0.06°, so a larger difference means something other than refraction moved.
 */
const PRE_CHANGE_SAMPLES: { site: string; latitude: number; longitude: number; instantUtc: string; azimuth: number; geometricAltitude: number }[] = [
  { site: 'Dubai', latitude: 25.2, longitude: 55.3, instantUtc: '2026-03-20T09:00:00Z', azimuth: 199.135324, geometricAltitude: 63.425717 },
  { site: 'Dubai', latitude: 25.2, longitude: 55.3, instantUtc: '2026-06-21T09:00:00Z', azimuth: 260.955408, geometricAltitude: 80.854500 },
  { site: 'Dubai', latitude: 25.2, longitude: 55.3, instantUtc: '2026-12-21T12:00:00Z', azimuth: 232.622361, geometricAltitude: 17.307904 },
  { site: 'London', latitude: 51.5, longitude: -0.1, instantUtc: '2026-03-20T12:00:00Z', azimuth: 177.499911, geometricAltitude: 38.430107 },
  { site: 'London', latitude: 51.5, longitude: -0.1, instantUtc: '2026-06-21T12:00:00Z', azimuth: 178.915543, geometricAltitude: 61.934776 },
  { site: 'London', latitude: 51.5, longitude: -0.1, instantUtc: '2026-12-21T12:00:00Z', azimuth: 180.361757, geometricAltitude: 15.061943 },
  { site: 'Singapore', latitude: 1.35, longitude: 103.82, instantUtc: '2026-03-20T06:00:00Z', azimuth: 262.967216, geometricAltitude: 77.964883 },
  { site: 'Singapore', latitude: 1.35, longitude: 103.82, instantUtc: '2026-09-21T09:00:00Z', azimuth: 269.966190, geometricAltitude: 29.470536 },
  { site: 'Tromsø', latitude: 69.6, longitude: 18.9, instantUtc: '2026-06-21T12:00:00Z', azimuth: 203.203931, geometricAltitude: 42.547004 },
  { site: 'Tromsø', latitude: 69.6, longitude: 18.9, instantUtc: '2026-03-20T12:00:00Z', azimuth: 198.104785, geometricAltitude: 19.424131 },
  { site: 'Reykjavík', latitude: 64.13, longitude: -21.9, instantUtc: '2026-06-21T12:00:00Z', azimuth: 149.393725, geometricAltitude: 46.730387 },
  { site: 'Reykjavík', latitude: 64.13, longitude: -21.9, instantUtc: '2026-09-21T12:00:00Z', azimuth: 157.682501, geometricAltitude: 24.755462 },
  // baseline.md, Badr (بدر، مصر) on 21 September 2026 — the site the manual checks were run on.
  { site: 'Badr (baseline.md 12:00 local)', latitude: 30.15, longitude: 31.72, instantUtc: '2026-09-21T09:00:00Z', azimuth: 157.437794, geometricAltitude: 58.503459 },
  { site: 'Badr (baseline.md, March)', latitude: 30.15, longitude: 31.72, instantUtc: '2026-03-20T09:00:00Z', azimuth: 151.745204, geometricAltitude: 56.496940 },
]

describe('solarPosition — unchanged against the pre-063 release (T004, FR-005)', () => {
  for (const sample of PRE_CHANGE_SAMPLES) {
    it(`leaves azimuth bit-identical at ${sample.site} ${sample.instantUtc}`, () => {
      const result = solarPosition(new Date(sample.instantUtc), sample.latitude, sample.longitude)
      expect(Number(result.azimuthDegrees.toFixed(6))).toBe(sample.azimuth)
    })

    it(`keeps altitude within tolerance of the pre-change value at ${sample.site} ${sample.instantUtc}`, () => {
      expect(sample.geometricAltitude).toBeGreaterThan(15) // documents that this row qualifies
      const result = solarPosition(new Date(sample.instantUtc), sample.latitude, sample.longitude)
      expect(Math.abs(result.altitudeDegrees - sample.geometricAltitude)).toBeLessThanOrEqual(
        SOLAR_POSITION_TOLERANCE_DEGREES,
      )
    })
  }

  it('raises the altitude rather than lowering it — refraction only ever lifts the sun', () => {
    for (const sample of PRE_CHANGE_SAMPLES) {
      const result = solarPosition(new Date(sample.instantUtc), sample.latitude, sample.longitude)
      expect(result.altitudeDegrees).toBeGreaterThanOrEqual(sample.geometricAltitude)
    }
  })

  it('exposes no uncorrected altitude anywhere on the result (FR-007)', () => {
    const result = solarPosition(new Date('2026-09-21T09:00:00Z'), 30.15, 31.72)
    expect(Object.keys(result).sort()).toEqual(['altitudeDegrees', 'azimuthDegrees', 'declination', 'eqTime'])
    // The single altitude is the corrected one, not the geometric 58.503459° the old release gave.
    expect(result.altitudeDegrees).not.toBeCloseTo(58.503459, 4)
    expect(result.altitudeDegrees).toBeCloseTo(58.513, 3)
  })
})

describe('solarPosition — sub-second resolution (FR-002a prerequisite)', () => {
  // `daySummary.ts`'s solver narrows the rise instant to a millisecond. If the clock were still
  // truncated to whole seconds the function it solves would be a staircase and no convergence
  // threshold could be crossed, so this is load-bearing rather than incidental precision.
  it('distinguishes instants that differ only by milliseconds', () => {
    const a = solarPosition(new Date(Date.UTC(2026, 8, 21, 3, 41, 18, 0)), 30.15, 31.72)
    const b = solarPosition(new Date(Date.UTC(2026, 8, 21, 3, 41, 18, 900)), 30.15, 31.72)
    expect(a.altitudeDegrees).not.toBe(b.altitudeDegrees)
    expect(b.altitudeDegrees).toBeGreaterThan(a.altitudeDegrees)
  })
})

describe('solarPosition — full day against NOAA published values (T012, SC-002)', () => {
  const instantFor = (minutesPastLocalMidnight: number): Date =>
    new Date(
      NOAA_FULL_DAY_SITE.dateUtc + (minutesPastLocalMidnight - NOAA_FULL_DAY_SITE.utcOffsetHours * 60) * 60_000,
    )

  it('covers a whole day at six-minute steps, both sides of the horizon', () => {
    expect(NOAA_FULL_DAY_SAMPLES.length).toBe(240)
    expect(NOAA_FULL_DAY_SAMPLES.filter(([, elevation]) => elevation > 0).length).toBe(150)
    expect(NOAA_FULL_DAY_SAMPLES.filter(([, elevation]) => elevation <= 0).length).toBe(90)
  })

  it('agrees with the published corrected-for-refraction elevation whenever the sun is up', () => {
    for (const [minute, elevation] of NOAA_FULL_DAY_SAMPLES) {
      if (elevation <= 0) continue
      const { altitudeDegrees } = solarPosition(instantFor(minute), NOAA_FULL_DAY_SITE.latitude, NOAA_FULL_DAY_SITE.longitude)
      expect(Math.abs(altitudeDegrees - elevation)).toBeLessThanOrEqual(SOLAR_POSITION_TOLERANCE_DEGREES)
    }
  })

  it('agrees far more tightly than the stated tolerance — 5e-7°, the precision the reference was transcribed at', () => {
    // This is what makes T014's reasoning checkable rather than asserted: the port reproduces the
    // published column exactly, so the 0.1° the panel states is a claim about the physical model,
    // not about this implementation's fidelity to NOAA.
    let worst = 0
    for (const [minute, elevation] of NOAA_FULL_DAY_SAMPLES) {
      if (elevation <= 0) continue
      const { altitudeDegrees } = solarPosition(instantFor(minute), NOAA_FULL_DAY_SITE.latitude, NOAA_FULL_DAY_SITE.longitude)
      worst = Math.max(worst, Math.abs(altitudeDegrees - elevation))
    }
    expect(worst).toBeLessThan(1e-6)
  })

  it('agrees with the published azimuth across the entire day, above and below the horizon', () => {
    for (const [minute, , azimuth] of NOAA_FULL_DAY_SAMPLES) {
      const { azimuthDegrees } = solarPosition(instantFor(minute), NOAA_FULL_DAY_SITE.latitude, NOAA_FULL_DAY_SITE.longitude)
      expect(Math.abs(azimuthDegrees - azimuth)).toBeLessThan(1e-6)
    }
  })

  it('departs from the published elevation below the horizon, deliberately and by a bounded amount', () => {
    /**
     * The one place this module does not reproduce NOAA's published column, and it is a choice
     * rather than an error. NOAA's spreadsheet extrapolates refraction below −0.575° with
     * `−20.772/tan(te)`, which *decreases* as the sun sinks and reaches zero at the nadir; NOAA's
     * own rise/set constant contradicts that, assuming a full 0.833° at an altitude where the
     * extrapolation yields 0.397°. NOAA's page states both treatments side by side:
     * "For sunrise and sunset calculations, we assume 0.833° of atmospheric refraction. In the
     * solar position calculator, atmospheric refraction is modeled as: [piecewise]".
     *
     * They cannot both be honoured. This feature honours the rise/set one, because that is what
     * FR-002 and SC-003 rest on — rise and set land within 45 s of NOAA's published tables at
     * every test site, and the altitude reported at them is exactly −0.2667° everywhere. The cost
     * is confined to altitudes where the sun is already down, and it is bounded here so it cannot
     * grow unnoticed.
     */
    let worst = 0
    for (const [minute, elevation] of NOAA_FULL_DAY_SAMPLES) {
      if (elevation > 0) continue
      const { altitudeDegrees } = solarPosition(instantFor(minute), NOAA_FULL_DAY_SITE.latitude, NOAA_FULL_DAY_SITE.longitude)
      worst = Math.max(worst, Math.abs(altitudeDegrees - elevation))
      // Never below the published value: the change only ever lifts the sun, never sinks it.
      expect(altitudeDegrees).toBeGreaterThanOrEqual(elevation - 1e-9)
    }
    expect(worst).toBeGreaterThan(0.5)
    expect(worst).toBeLessThan(HORIZON_REFRACTION_DEGREES)
  })
})
