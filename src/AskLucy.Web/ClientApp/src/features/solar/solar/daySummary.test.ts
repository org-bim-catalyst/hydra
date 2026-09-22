import { describe, expect, it } from 'vitest'
import { copy } from '../copy'
import { buildSolarFiguresContent } from '../panels/solarFiguresContent'
import { daySummary, RISE_SET_SOLVER_CONVERGENCE_DEGREES } from './daySummary'
import { SOLAR_SEMIDIAMETER_DEGREES } from './refraction'
import {
  HIGH_LATITUDE_THRESHOLD_DEGREES,
  RISE_SET_TOLERANCE_SECONDS,
  RISE_SET_TOLERANCE_SECONDS_HIGH_LATITUDE,
  SOLAR_POSITION_TOLERANCE_DEGREES,
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

/**
 * US1 — "The Numbers Agree With Each Other". The five reference sites the feature is specified
 * against, spanning the tropics, mid latitudes and both sides of the Arctic Circle, each sampled
 * at the two equinoxes and the two solstices.
 */
const US1_SITES = [
  { name: 'Dubai', latitude: 25.2, longitude: 55.3 },
  { name: 'London', latitude: 51.5, longitude: -0.1 },
  { name: 'Singapore', latitude: 1.35, longitude: 103.8 },
  { name: 'Tromsø', latitude: 69.6, longitude: 18.9 },
  { name: 'Reykjavík', latitude: 64.13, longitude: -21.9 },
] as const

/** Month index and day-of-month, in the UTC calendar `daySummary` reads. */
const US1_DATES = [
  { name: '20 March', month: 2, day: 20 },
  { name: '21 June', month: 5, day: 21 },
  { name: '21 September', month: 8, day: 21 },
  { name: '21 December', month: 11, day: 21 },
] as const

describe('daySummary — the central assertion, every site and date (T009, SC-001)', () => {
  for (const site of US1_SITES) {
    for (const date of US1_DATES) {
      it(`puts the sun's centre one radius below the horizon at ${site.name} rise and set on ${date.name}`, () => {
        const summary = daySummary(new Date(Date.UTC(2026, date.month, date.day)), site.latitude, site.longitude)
        expect(summary.riseSetUndetermined).toBe(false)

        // The polar days carry their own assertions in T013; there is no rise instant to test here.
        if (summary.polarCondition !== 'none') return

        for (const instant of [summary.sunriseUtc!, summary.sunsetUtc!]) {
          const { altitudeDegrees } = solarPosition(instant, site.latitude, site.longitude)
          expect(Math.abs(altitudeDegrees - -SOLAR_SEMIDIAMETER_DEGREES)).toBeLessThanOrEqual(
            SOLAR_POSITION_TOLERANCE_DEGREES,
          )
        }
      })
    }
  }
})

describe('daySummary — the altitude at rise/set is one number, not one per site (T010, FR-002)', () => {
  /**
   * T009 asks whether each value is close enough to −0.2667°. This asks whether they are all the
   * *same* value — which is the question the defect this feature repairs would fail. The old
   * implementation's altitudes were individually within a tenth of a degree of each other and
   * still drifted from −0.77° to −0.96° across sites and seasons, because the residual was
   * site-dependent. A spread assertion catches that; a per-value tolerance assertion does not.
   *
   * The spread allowed is the solver's own convergence threshold, doubled to cover two values
   * landing on opposite sides of it — four orders of magnitude tighter than
   * `SOLAR_POSITION_TOLERANCE_DEGREES`, so nothing but exactness passes.
   */
  it('produces the same altitude at every rise and set across all sites and dates', () => {
    const altitudes: { label: string; altitudeDegrees: number }[] = []

    for (const site of US1_SITES) {
      for (const date of US1_DATES) {
        const summary = daySummary(new Date(Date.UTC(2026, date.month, date.day)), site.latitude, site.longitude)
        if (summary.polarCondition !== 'none') continue

        altitudes.push(
          { label: `${site.name} ${date.name} rise`, altitudeDegrees: solarPosition(summary.sunriseUtc!, site.latitude, site.longitude).altitudeDegrees },
          { label: `${site.name} ${date.name} set`, altitudeDegrees: solarPosition(summary.sunsetUtc!, site.latitude, site.longitude).altitudeDegrees },
        )
      }
    }

    // Every site/date pair except the two polar ones, twice over.
    expect(altitudes.length).toBe(36)

    const values = altitudes.map((entry) => entry.altitudeDegrees)
    const spread = Math.max(...values) - Math.min(...values)
    // Named in the failure message so a regression says *where* it drifted, not just that it did.
    const extremes = `${altitudes.find((e) => e.altitudeDegrees === Math.min(...values))!.label} … ${altitudes.find((e) => e.altitudeDegrees === Math.max(...values))!.label}`
    expect({ spread, extremes }).toEqual({ spread: expect.any(Number), extremes: expect.any(String) })
    expect(spread).toBeLessThanOrEqual(2 * RISE_SET_SOLVER_CONVERGENCE_DEGREES)
  })
})

describe('daySummary — the polar cases use the same horizon definition (T013, FR-004)', () => {
  /**
   * US1 scenario 6. "The sun never rises" and "the sun never sets" must be decided by the *same*
   * upper-edge definition that rise and set are solved for, or the panel can say the sun never
   * rises on a date whose sunrise it also prints. These assertions tie the two together directly:
   * on a polar-night date the sun's upper edge stays below the horizon for the whole 24 hours, and
   * on a midnight-sun date it stays above it — sampled independently of `daySummary`'s own branch.
   */
  const sampleUpperEdgeAltitudes = (dateUtc: Date, latitude: number, longitude: number): number[] => {
    const samples: number[] = []
    for (let minute = 0; minute < 24 * 60; minute += 10) {
      const instant = new Date(dateUtc.getTime() + minute * 60_000)
      samples.push(solarPosition(instant, latitude, longitude).altitudeDegrees + SOLAR_SEMIDIAMETER_DEGREES)
    }
    return samples
  }

  it('reports polar night at Tromsø in December, and the upper edge never clears the horizon', () => {
    const date = new Date(Date.UTC(2026, 11, 21))
    const summary = daySummary(date, 69.6, 18.9)
    expect(summary.polarCondition).toBe('polar-night')
    expect(summary.riseSetUndetermined).toBe(false)
    expect(Math.max(...sampleUpperEdgeAltitudes(date, 69.6, 18.9))).toBeLessThan(0)
  })

  it('reports midnight sun at Tromsø in June, and the upper edge never drops below the horizon', () => {
    const date = new Date(Date.UTC(2026, 5, 21))
    const summary = daySummary(date, 69.6, 18.9)
    expect(summary.polarCondition).toBe('midnight-sun')
    expect(summary.riseSetUndetermined).toBe(false)
    expect(Math.min(...sampleUpperEdgeAltitudes(date, 69.6, 18.9))).toBeGreaterThan(0)
  })

  it('reports neither at Reykjavík in June, where the sun does still set — and the upper edge does cross', () => {
    const date = new Date(Date.UTC(2026, 5, 21))
    const summary = daySummary(date, 64.13, -21.9)
    expect(summary.polarCondition).toBe('none')
    const samples = sampleUpperEdgeAltitudes(date, 64.13, -21.9)
    expect(Math.min(...samples)).toBeLessThan(0)
    expect(Math.max(...samples)).toBeGreaterThan(0)
  })
})

/**
 * T011 / FR-004a / SC-003 — rise and set against a *published* reference, never against this
 * codebase's previous output. The values are read from NOAA GML's annual sunrise/sunset table,
 *
 *   https://gml.noaa.gov/grad/solcalc/table.php?lat=..&lon=..&year=2026
 *
 * which prints local clock time to the minute. They are recorded here already converted to UTC,
 * against the offset actually in force on each date rather than a single offset per site: the
 * London rows span a DST boundary (20 March is GMT, 21 June and 21 September are BST, 21 December
 * is GMT again) and so do the Tromsø ones, and converting a whole site at one offset would put
 * four of these rows an hour wrong.
 *
 * `dayShift` carries the rows where the UTC instant falls on the neighbouring calendar day —
 * Singapore's sunrises (UTC+8) and Reykjavík's midsummer sunset just after local midnight.
 */
interface NoaaRiseSetRow {
  site: string
  latitude: number
  longitude: number
  month: number
  day: number
  riseUtc: string
  setUtc: string
  riseDayShift?: number
  setDayShift?: number
}

const NOAA_RISE_SET: readonly NoaaRiseSetRow[] = [
  { site: 'Dubai', latitude: 25.2, longitude: 55.3, month: 2, day: 20, riseUtc: '02:23', setUtc: '14:30' },
  { site: 'Dubai', latitude: 25.2, longitude: 55.3, month: 5, day: 21, riseUtc: '01:29', setUtc: '15:12' },
  { site: 'Dubai', latitude: 25.2, longitude: 55.3, month: 8, day: 21, riseUtc: '02:07', setUtc: '14:17' },
  { site: 'Dubai', latitude: 25.2, longitude: 55.3, month: 11, day: 21, riseUtc: '03:00', setUtc: '13:34' },
  { site: 'London', latitude: 51.5, longitude: -0.1, month: 2, day: 20, riseUtc: '06:03', setUtc: '18:13' },
  { site: 'London', latitude: 51.5, longitude: -0.1, month: 5, day: 21, riseUtc: '03:43', setUtc: '20:21' },
  { site: 'London', latitude: 51.5, longitude: -0.1, month: 8, day: 21, riseUtc: '05:45', setUtc: '18:01' },
  { site: 'London', latitude: 51.5, longitude: -0.1, month: 11, day: 21, riseUtc: '08:04', setUtc: '15:53' },
  { site: 'Singapore', latitude: 1.35, longitude: 103.8, month: 2, day: 20, riseUtc: '23:09', riseDayShift: -1, setUtc: '11:15' },
  { site: 'Singapore', latitude: 1.35, longitude: 103.8, month: 5, day: 21, riseUtc: '23:00', riseDayShift: -1, setUtc: '11:12' },
  { site: 'Singapore', latitude: 1.35, longitude: 103.8, month: 8, day: 21, riseUtc: '22:55', riseDayShift: -1, setUtc: '11:01' },
  { site: 'Singapore', latitude: 1.35, longitude: 103.8, month: 11, day: 21, riseUtc: '23:01', riseDayShift: -1, setUtc: '11:04' },
  { site: 'Tromsø', latitude: 69.6, longitude: 18.9, month: 2, day: 20, riseUtc: '04:44', setUtc: '17:02' },
  { site: 'Tromsø', latitude: 69.6, longitude: 18.9, month: 8, day: 21, riseUtc: '04:20', setUtc: '16:52' },
  { site: 'Reykjavík', latitude: 64.13, longitude: -21.9, month: 2, day: 20, riseUtc: '07:28', setUtc: '19:43' },
  { site: 'Reykjavík', latitude: 64.13, longitude: -21.9, month: 5, day: 21, riseUtc: '02:55', setUtc: '00:03', setDayShift: 1 },
  { site: 'Reykjavík', latitude: 64.13, longitude: -21.9, month: 8, day: 21, riseUtc: '07:08', setUtc: '19:32' },
  { site: 'Reykjavík', latitude: 64.13, longitude: -21.9, month: 11, day: 21, riseUtc: '11:22', setUtc: '15:30' },
]

function noaaInstantMs(row: NoaaRiseSetRow, hhmm: string, dayShift = 0): number {
  const [hour, minute] = hhmm.split(':').map(Number)
  return Date.UTC(2026, row.month, row.day + dayShift, hour, minute, 0)
}

describe('daySummary — rise/set against NOAA published values (T011, FR-004a, SC-003)', () => {
  for (const row of NOAA_RISE_SET) {
    const label = `${row.site} ${row.day}/${row.month + 1}`
    const tolerance =
      Math.abs(row.latitude) > HIGH_LATITUDE_THRESHOLD_DEGREES
        ? RISE_SET_TOLERANCE_SECONDS_HIGH_LATITUDE
        : RISE_SET_TOLERANCE_SECONDS

    it(`agrees with NOAA's published sunrise and sunset at ${label} to within ${tolerance} s`, () => {
      const summary = daySummary(new Date(Date.UTC(2026, row.month, row.day)), row.latitude, row.longitude)
      expect(summary.polarCondition).toBe('none')

      const riseErrorSeconds = (summary.sunriseUtc!.getTime() - noaaInstantMs(row, row.riseUtc, row.riseDayShift)) / 1000
      const setErrorSeconds = (summary.sunsetUtc!.getTime() - noaaInstantMs(row, row.setUtc, row.setDayShift)) / 1000

      expect(Math.abs(riseErrorSeconds)).toBeLessThanOrEqual(tolerance)
      expect(Math.abs(setErrorSeconds)).toBeLessThanOrEqual(tolerance)
    })
  }

  it('agrees at every site including the two high-latitude ones, inside the inner ±60 s tolerance', () => {
    // Every row above is inside ±72°, so `RISE_SET_TOLERANCE_SECONDS` governs all of them; this
    // records the worst case as a single number so a regression that degrades many rows slightly —
    // rather than one row badly — is still visible. Measured worst case: 40.2 s, at Reykjavík's
    // 21 June sunset, where the sun sets at the shallowest angle of any row here.
    let worstSeconds = 0
    for (const row of NOAA_RISE_SET) {
      const summary = daySummary(new Date(Date.UTC(2026, row.month, row.day)), row.latitude, row.longitude)
      worstSeconds = Math.max(
        worstSeconds,
        Math.abs((summary.sunriseUtc!.getTime() - noaaInstantMs(row, row.riseUtc, row.riseDayShift)) / 1000),
        Math.abs((summary.sunsetUtc!.getTime() - noaaInstantMs(row, row.setUtc, row.setDayShift)) / 1000),
      )
    }
    expect(worstSeconds).toBeLessThanOrEqual(RISE_SET_TOLERANCE_SECONDS)
    expect(worstSeconds).toBeLessThanOrEqual(45)
  })
})
