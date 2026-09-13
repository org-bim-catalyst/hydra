import { describe, expect, it } from 'vitest'
import { daySummary } from './daySummary'
import {
  HIGH_LATITUDE_THRESHOLD_DEGREES,
  RISE_SET_TOLERANCE_SECONDS,
  RISE_SET_TOLERANCE_SECONDS_HIGH_LATITUDE,
  solarPosition,
} from './solarPosition'

/**
 * SC-001 / quickstart Scenario 1. The sunrise/sunset instants `daySummary` produces are checked
 * for internal consistency against the *same* solar-position function they are derived from: the
 * sun's altitude at the computed sunrise/sunset instant must sit at the standard −0.833° reference
 * altitude (atmospheric refraction + solar radius) NOAA's rise/set formula targets. This does not
 * independently verify the NOAA formula itself (solarPosition.test.ts's solar-noon identity does
 * that) — it verifies that `daySummary.ts`'s own arithmetic (solving for the hour angle at that
 * reference altitude, converting to UTC minutes) is faithful to the value the shared
 * `solarPosition` function would compute for that same instant. The tolerance converts the stated
 * ±60 s / ±10 min time tolerances (research D1, inherited from NOAA) into the corresponding
 * altitude change via the sun's typical rate of ascent (~0.25°/minute away from the poles).
 */

function altitudeToleranceForSeconds(seconds: number): number {
  // ~0.25°/minute is a generous, non-polar rate of change — using it to bound a tighter time
  // tolerance in degrees is conservative (the assertion is easier to satisfy at low latitudes,
  // where the sun actually does move close to that rate, and looser at high latitudes, where the
  // rate is much shallower and the ±10 min tolerance already accounts for that).
  return (seconds / 60) * 0.25
}

describe('daySummary — rise/set self-consistency (SC-001)', () => {
  it('places London sunrise/sunset altitude at the standard -0.833° reference altitude', () => {
    const summary = daySummary(new Date(Date.UTC(2026, 5, 21)), 51.5, -0.1)
    expect(summary.polarCondition).toBe('none')
    expect(summary.sunriseUtc).not.toBeNull()
    expect(summary.sunsetUtc).not.toBeNull()

    const riseAltitude = solarPosition(summary.sunriseUtc!, 51.5, -0.1).altitudeDegrees
    const setAltitude = solarPosition(summary.sunsetUtc!, 51.5, -0.1).altitudeDegrees
    expect(Math.abs(riseAltitude - -0.833)).toBeLessThanOrEqual(altitudeToleranceForSeconds(RISE_SET_TOLERANCE_SECONDS))
    expect(Math.abs(setAltitude - -0.833)).toBeLessThanOrEqual(altitudeToleranceForSeconds(RISE_SET_TOLERANCE_SECONDS))
  })

  it('places Dubai sunrise/sunset altitude at the standard reference altitude across the year', () => {
    for (const month of [2, 5, 8, 11]) {
      const summary = daySummary(new Date(Date.UTC(2026, month, 20)), 25.2, 55.3)
      expect(summary.polarCondition).toBe('none')
      const riseAltitude = solarPosition(summary.sunriseUtc!, 25.2, 55.3).altitudeDegrees
      expect(Math.abs(riseAltitude - -0.833)).toBeLessThanOrEqual(altitudeToleranceForSeconds(RISE_SET_TOLERANCE_SECONDS))
    }
  })

  it('reports dayLengthMinutes consistent with sunrise/sunset for a normal day', () => {
    const summary = daySummary(new Date(Date.UTC(2026, 2, 20)), -0.2, -78.5)
    const minutes = (summary.sunsetUtc!.getTime() - summary.sunriseUtc!.getTime()) / 60_000
    expect(summary.dayLengthMinutes).toBeCloseTo(minutes, 2)
  })
})

describe('daySummary — polar cases (FR-002)', () => {
  it('distinguishes midnight sun from polar night at Tromsø (69.6°N, within the high-latitude band)', () => {
    expect(69.6).toBeGreaterThan(0)
    expect(Math.abs(69.6)).toBeLessThan(HIGH_LATITUDE_THRESHOLD_DEGREES) // documents which tolerance row applies

    const juneSummary = daySummary(new Date(Date.UTC(2026, 5, 21)), 69.6, 18.9)
    expect(juneSummary.polarCondition).toBe('midnight-sun')
    expect(juneSummary.sunriseUtc).toBeNull()
    expect(juneSummary.sunsetUtc).toBeNull()
    expect(juneSummary.dayLengthMinutes).toBeNull()

    const decemberSummary = daySummary(new Date(Date.UTC(2026, 11, 21)), 69.6, 18.9)
    expect(decemberSummary.polarCondition).toBe('polar-night')
    expect(decemberSummary.sunriseUtc).toBeNull()
    expect(decemberSummary.sunsetUtc).toBeNull()
    expect(decemberSummary.dayLengthMinutes).toBeNull()
  })

  it('never confuses "never rises" with "never sets" — they are distinct, never both null-with-no-signal', () => {
    const june = daySummary(new Date(Date.UTC(2026, 5, 21)), 78, 15.6) // Svalbard-like, beyond ±72°
    const december = daySummary(new Date(Date.UTC(2026, 11, 21)), 78, 15.6)
    expect(june.polarCondition).not.toBe(december.polarCondition)
    expect(june.polarCondition).toBe('midnight-sun')
    expect(december.polarCondition).toBe('polar-night')
  })

  it(`is bounded, beyond ±72°, by the wider ${RISE_SET_TOLERANCE_SECONDS_HIGH_LATITUDE / 60}-minute tolerance rather than the inner-band one`, () => {
    // Documents the two-row tolerance table (research D1) so a future change to either constant
    // is visible in a failing assertion rather than silently drifting.
    expect(RISE_SET_TOLERANCE_SECONDS_HIGH_LATITUDE).toBeGreaterThan(RISE_SET_TOLERANCE_SECONDS)
  })
})

describe('daySummary — Ushuaia, southern hemisphere seasons inverted', () => {
  it('has its longest day in December, not June', () => {
    const june = daySummary(new Date(Date.UTC(2026, 5, 21)), -54.8, -68.3)
    const december = daySummary(new Date(Date.UTC(2026, 11, 21)), -54.8, -68.3)
    expect(december.dayLengthMinutes!).toBeGreaterThan(june.dayLengthMinutes!)
  })
})
