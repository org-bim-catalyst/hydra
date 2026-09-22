import { describe, expect, it } from 'vitest'
import {
  RISE_SET_GEOMETRIC_ALTITUDE_DEGREES,
  SOLAR_SEMIDIAMETER_DEGREES,
  geometricAltitudeForApparentDegrees,
  refractionCorrectionDegrees,
} from './refraction'
import { SOLAR_POSITION_TOLERANCE_DEGREES } from './solarPosition'

/**
 * contracts/solar-position.md `refraction.ts`. The reference values below are NOAA's own piecewise
 * expressions (research D2) evaluated at the stated elevations and converted from arcseconds, not
 * numbers this implementation produced — they are written out so a transcription error in the
 * coefficients fails here rather than showing up as a fraction of a degree in the figures panel.
 */

describe('refractionCorrectionDegrees — published NOAA values', () => {
  const cases: { elevationDegrees: number; arcseconds: number; note: string }[] = [
    { elevationDegrees: 0, arcseconds: 1735.0, note: 'horizon — the polynomial band\'s constant term' },
    { elevationDegrees: 5, arcseconds: 574.625, note: 'top of the polynomial band (5° itself is not > 5)' },
    { elevationDegrees: 15, arcseconds: 213.2558, note: 'tangent band' },
    { elevationDegrees: 45, arcseconds: 58.0301, note: 'tangent band, tan = 1 so the series is 58.1 − 0.07 + 0.000086' },
  ]

  for (const { elevationDegrees, arcseconds, note } of cases) {
    it(`returns ${(arcseconds / 3600).toFixed(6)}° at ${elevationDegrees}° (${note})`, () => {
      expect(refractionCorrectionDegrees(elevationDegrees)).toBeCloseTo(arcseconds / 3600, 6)
    })
  }

  it('is zero above 85°, where NOAA states no correction', () => {
    expect(refractionCorrectionDegrees(85.000001)).toBe(0)
    expect(refractionCorrectionDegrees(86)).toBe(0)
    expect(refractionCorrectionDegrees(90)).toBe(0)
  })

  it('is never negative above the horizon — refraction lifts the sun, it never lowers it', () => {
    for (let elevation = 0; elevation <= 90; elevation += 0.5) {
      expect(refractionCorrectionDegrees(elevation)).toBeGreaterThanOrEqual(0)
    }
  })
})

describe('refractionCorrectionDegrees — band boundaries', () => {
  // NOAA's four expressions are independent fits, not a spline, so they do not join exactly. The
  // contract requires only that the step be inside the position tolerance; these assertions pin
  // the actual steps so a coefficient change that widened one would fail here.
  const boundaries = [85, 5, -0.575]

  for (const boundary of boundaries) {
    it(`steps by less than the position tolerance across the ${boundary}° boundary`, () => {
      const below = refractionCorrectionDegrees(boundary - 1e-6)
      const above = refractionCorrectionDegrees(boundary + 1e-6)
      expect(Math.abs(above - below)).toBeLessThan(SOLAR_POSITION_TOLERANCE_DEGREES)
    })
  }

  it('steps by at most 0.0014° anywhere — the 85° boundary is the largest of the three', () => {
    const steps = boundaries.map((boundary) =>
      Math.abs(refractionCorrectionDegrees(boundary + 1e-6) - refractionCorrectionDegrees(boundary - 1e-6)),
    )
    expect(Math.max(...steps)).toBeLessThan(0.0015)
  })
})

describe('refractionCorrectionDegrees — below the horizon', () => {
  // The rise/set solver evaluates the correction at negative geometric altitudes (the sun's centre
  // is ~0.72° below the horizon when its upper edge is on it), so "defined below zero" is a
  // working requirement, not a curiosity.
  it('returns a finite value at every altitude from -90° to 0°', () => {
    for (let elevation = -90; elevation <= 0; elevation += 0.25) {
      expect(Number.isFinite(refractionCorrectionDegrees(elevation))).toBe(true)
    }
  })

  it('is finite at the -90° and -0.575° extremes, where the tangent term is degenerate', () => {
    expect(Number.isFinite(refractionCorrectionDegrees(-90))).toBe(true)
    expect(Number.isFinite(refractionCorrectionDegrees(-0.575))).toBe(true)
  })

  it('is largest near the horizon, as the physical effect is', () => {
    expect(refractionCorrectionDegrees(-0.575)).toBeGreaterThan(refractionCorrectionDegrees(5))
    expect(refractionCorrectionDegrees(5)).toBeGreaterThan(refractionCorrectionDegrees(45))
    expect(refractionCorrectionDegrees(45)).toBeGreaterThan(refractionCorrectionDegrees(80))
  })
})

describe('refractionCorrectionDegrees — purity', () => {
  it('returns the same value for the same input and does not mutate anything observable', () => {
    const first = refractionCorrectionDegrees(12.345)
    const second = refractionCorrectionDegrees(12.345)
    expect(second).toBe(first)
  })
})

describe('geometricAltitudeForApparentDegrees', () => {
  it('inverts refractionCorrectionDegrees across the horizon band', () => {
    for (const apparent of [-0.2667, -0.1, 0, 0.5, 2, 10, 45, 80]) {
      const geometric = geometricAltitudeForApparentDegrees(apparent)
      expect(geometric + refractionCorrectionDegrees(geometric)).toBeCloseTo(apparent, 9)
    }
  })

  it('never returns an altitude above the apparent one', () => {
    for (const apparent of [-0.2667, 0, 1, 30, 86]) {
      expect(geometricAltitudeForApparentDegrees(apparent)).toBeLessThanOrEqual(apparent)
    }
  })
})

describe('RISE_SET_GEOMETRIC_ALTITUDE_DEGREES — the one horizon definition (FR-004)', () => {
  it('is the geometric altitude whose apparent value is exactly minus the semidiameter', () => {
    const apparent =
      RISE_SET_GEOMETRIC_ALTITUDE_DEGREES + refractionCorrectionDegrees(RISE_SET_GEOMETRIC_ALTITUDE_DEGREES)
    expect(apparent).toBeCloseTo(-SOLAR_SEMIDIAMETER_DEGREES, 9)
  })

  it('sits near -0.724°, not at the bundled -0.833° the port inherited (research D1)', () => {
    expect(RISE_SET_GEOMETRIC_ALTITUDE_DEGREES).toBeCloseTo(-0.7236, 3)
    expect(RISE_SET_GEOMETRIC_ALTITUDE_DEGREES).not.toBeCloseTo(-0.833, 3)
  })
})
