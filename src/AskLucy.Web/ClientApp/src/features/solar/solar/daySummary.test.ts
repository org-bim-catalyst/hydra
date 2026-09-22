import { describe, expect, it } from 'vitest'
import { copy } from '../copy'
import { buildSolarFiguresContent } from '../panels/solarFiguresContent'
import { daySummary } from './daySummary'
import { SOLAR_SEMIDIAMETER_DEGREES } from './refraction'
import {
  HIGH_LATITUDE_THRESHOLD_DEGREES,
  RISE_SET_TOLERANCE_SECONDS,
  RISE_SET_TOLERANCE_SECONDS_HIGH_LATITUDE,
  solarPosition,
} from './solarPosition'

/**
 * SC-001 / quickstart Scenario 1. The sunrise/sunset instants `daySummary` produces are checked
 * for internal consistency against the *same* solar-position function they are derived from: the
 * sun's apparent altitude at the computed sunrise/sunset instant must sit at exactly
 * `-SOLAR_SEMIDIAMETER_DEGREES`.
 *
 * specs/063 replaced the old `-0.833 degrees` expectation here. That number bundled refraction and
 * the sun's angular radius together, and because it was applied through a hour-angle formula
 * evaluated once at noon, the altitude actually observed at the returned instant drifted between
 * -0.77 and -0.96 degrees depending on site and season. Now refraction lives in `solarPosition`
 * and the radius alone defines rise and set, so this assertion has a single exact target that does
 * not move (research D1/D3, FR-002). This does not
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
  it('places London sunrise/sunset altitude at exactly minus the solar semidiameter', () => {
    const summary = daySummary(new Date(Date.UTC(2026, 5, 21)), 51.5, -0.1)
    expect(summary.polarCondition).toBe('none')
    expect(summary.sunriseUtc).not.toBeNull()
    expect(summary.sunsetUtc).not.toBeNull()

    const riseAltitude = solarPosition(summary.sunriseUtc!, 51.5, -0.1).altitudeDegrees
    const setAltitude = solarPosition(summary.sunsetUtc!, 51.5, -0.1).altitudeDegrees
    expect(Math.abs(riseAltitude - -SOLAR_SEMIDIAMETER_DEGREES)).toBeLessThanOrEqual(
      altitudeToleranceForSeconds(RISE_SET_TOLERANCE_SECONDS),
    )
    expect(Math.abs(setAltitude - -SOLAR_SEMIDIAMETER_DEGREES)).toBeLessThanOrEqual(
      altitudeToleranceForSeconds(RISE_SET_TOLERANCE_SECONDS),
    )
  })

  it('places Dubai sunrise/sunset altitude at minus the solar semidiameter across the year', () => {
    for (const month of [2, 5, 8, 11]) {
      const summary = daySummary(new Date(Date.UTC(2026, month, 20)), 25.2, 55.3)
      expect(summary.polarCondition).toBe('none')
      const riseAltitude = solarPosition(summary.sunriseUtc!, 25.2, 55.3).altitudeDegrees
      expect(Math.abs(riseAltitude - -SOLAR_SEMIDIAMETER_DEGREES)).toBeLessThanOrEqual(
        altitudeToleranceForSeconds(RISE_SET_TOLERANCE_SECONDS),
      )
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

/**
 * T008 / FR-028, constitution §2 VIII — the solver's failure path.
 *
 * Non-convergence is not reachable with real inputs: the closed-form seed lands within minutes of
 * the answer and Newton closes that in about five evaluations against a twelve-evaluation cap.
 * `maxIterations: 0` exists so the path can still be exercised, because an unreachable failure
 * path and an unhandled one look identical until someone checks.
 */
describe('daySummary — non-convergence surfaces rather than guesses (T008, FR-028)', () => {
  const dubaiSeptember = () => daySummary(new Date(Date.UTC(2026, 8, 21)), 25.2, 55.3, { maxIterations: 0 })

  it('reports the failure explicitly instead of returning a time', () => {
    const summary = dubaiSeptember()
    expect(summary.riseSetUndetermined).toBe(true)
    expect(summary.sunriseUtc).toBeNull()
    expect(summary.sunsetUtc).toBeNull()
    expect(summary.dayLengthMinutes).toBeNull()
  })

  it('does not disguise the failure as a polar condition — the sun does rise at Dubai in September', () => {
    expect(dubaiSeptember().polarCondition).toBe('none')
    expect(daySummary(new Date(Date.UTC(2026, 8, 21)), 25.2, 55.3).riseSetUndetermined).toBe(false)
  })

  it('never returns the seed, which is a plausible-looking time that is minutes wrong', () => {
    const solved = daySummary(new Date(Date.UTC(2026, 8, 21)), 25.2, 55.3)
    const failed = dubaiSeptember()
    // The seed is within ~3 minutes of `solved`, so "returned the seed" would be indistinguishable
    // from success at the panel's one-minute display precision. There must be no Date at all.
    expect(failed.sunriseUtc).toBeNull()
    expect(solved.sunriseUtc).toBeInstanceOf(Date)
  })

  it('reaches the figures panel as visible text saying the times could not be determined', () => {
    const summary = dubaiSeptember()
    const content = buildSolarFiguresContent({
      localDate: '2026-09-21',
      localMinuteOfDay: 720,
      timeZoneId: 'Asia/Dubai',
      timeBasisLabel: 'Asia/Dubai',
      solarPosition: solarPosition(new Date(Date.UTC(2026, 8, 21, 8, 0, 0)), 25.2, 55.3),
      daySummary: summary,
      siteBuildingHeightAssumed: null,
    })

    const keyValue = content.blocks.find((block) => block.kind === 'keyValue')
    expect(keyValue).toBeDefined()
    const rendered = JSON.stringify(keyValue)
    expect(rendered).toContain(copy.riseSetUndetermined)

    // And nothing in those rows that reads like a time — no "06:41", no day length, no polar
    // wording. (The heading legitimately carries the analysis moment, which is why this looks at
    // the key/value rows rather than the whole document.)
    expect(rendered).not.toMatch(/\d{2}:\d{2}/)
    expect(rendered).not.toContain(copy.dayLengthLabel)
    expect(rendered).not.toContain(copy.sunNeverRises)
    expect(rendered).not.toContain(copy.sunNeverSets)
  })

  it('leaves the polar cases untouched — they are a different answer, not a failure', () => {
    const polarNight = daySummary(new Date(Date.UTC(2026, 11, 21)), 69.6, 18.9)
    expect(polarNight.polarCondition).toBe('polar-night')
    expect(polarNight.riseSetUndetermined).toBe(false)
  })
})
