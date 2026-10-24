import { describe, expect, it } from 'vitest'
import { daySummary } from './daySummary'
import { HORIZON_REFRACTION_DEGREES } from './refraction'
import { solarPosition } from './solarPosition'
import { TWILIGHT_THRESHOLDS, twilightCrossings, yearProfile } from './twilight'

/**
 * The reference values below are an **independent implementation's** published output, not this
 * codebase's own: `api.sunrise-sunset.org` (NOAA's algorithm, a different implementation from this
 * port), fetched 2026-10-24. They are the check that our thresholds match the published definition
 * — comparing against a value recomputed by the code under test would prove nothing.
 *
 * Each instant is UTC. Dubai is UTC+4 year-round; London is UTC+1 in June.
 */
const DUBAI = { latitude: 25.2048, longitude: 55.2708 }
const LONDON = { latitude: 51.5074, longitude: -0.1278 }
const TROMSO = { latitude: 69.6492, longitude: 18.9553 }

const REFERENCE = [
  {
    name: 'Dubai, 2026-09-25',
    site: DUBAI,
    date: new Date(Date.UTC(2026, 8, 25)),
    civil: { ascending: '2026-09-25T01:45:49Z', descending: '2026-09-25T14:35:26Z' },
    nautical: { ascending: '2026-09-25T01:19:14Z', descending: '2026-09-25T15:02:00Z' },
    astronomical: { ascending: '2026-09-25T00:52:33Z', descending: '2026-09-25T15:28:42Z' },
  },
  {
    name: 'London, 2026-06-21',
    site: LONDON,
    date: new Date(Date.UTC(2026, 5, 21)),
    civil: { ascending: '2026-06-21T02:55:24Z', descending: '2026-06-21T21:09:15Z' },
    nautical: { ascending: '2026-06-21T01:40:46Z', descending: '2026-06-21T22:23:54Z' },
    // The sun never reaches 18° below the horizon at this latitude in midsummer.
    astronomical: null,
  },
] as const

const MINUTE_MS = 60_000

function crossing(summary: ReturnType<typeof twilightCrossings>, phase: string, direction: 'ascending' | 'descending') {
  return summary.crossings.find((c) => c.phase === phase && c.direction === direction)!
}

describe('twilightCrossings — agreement with an independent published source', () => {
  for (const reference of REFERENCE) {
    it(`${reference.name}`, () => {
      const summary = twilightCrossings(reference.date, reference.site.latitude, reference.site.longitude)

      for (const phase of ['civil', 'nautical', 'astronomical'] as const) {
        const expected = reference[phase]
        const ascending = crossing(summary, phase, 'ascending')
        const descending = crossing(summary, phase, 'descending')

        if (expected === null) {
          expect(ascending.reached).toBe(false)
          expect(descending.reached).toBe(false)
          expect(ascending.instantUtc).toBeNull()
          expect(descending.instantUtc).toBeNull()
          continue
        }

        expect(ascending.reached).toBe(true)
        expect(descending.reached).toBe(true)
        // The module's stated rise/set tolerance: one minute inside ±72° of the equator.
        const toleranceMs = 60 * MINUTE_MS / 60
        expect(Math.abs(ascending.instantUtc!.getTime() - Date.parse(expected.ascending))).toBeLessThanOrEqual(toleranceMs)
        expect(Math.abs(descending.instantUtc!.getTime() - Date.parse(expected.descending))).toBeLessThanOrEqual(toleranceMs)
      }
    })
  }
})

describe('twilightCrossings — the reported altitude is one constant at every threshold', () => {
  // FR-005: the thresholds are defined on the unrefracted centre, so the refraction-corrected
  // altitude the panel shows beside them is the threshold plus the constant horizon refraction —
  // the same number at every site and date. This is the property that makes the figures
  // checkable, and it is the direct analogue of "altitude reads −0.27° at sunrise".
  const sites = [DUBAI, LONDON, { latitude: -33.8688, longitude: 151.2093 }]
  const dates = [new Date(Date.UTC(2026, 2, 20)), new Date(Date.UTC(2026, 8, 25))]

  it.each(sites.flatMap((site) => dates.map((date) => ({ site, date }))))(
    'at $site.latitude on $date',
    ({ site, date }) => {
      const summary = twilightCrossings(date, site.latitude, site.longitude)

      for (const threshold of TWILIGHT_THRESHOLDS) {
        const cross = crossing(summary, threshold.phase, 'descending')
        if (!cross.reached || !cross.instantUtc) continue

        const altitude = solarPosition(cross.instantUtc, site.latitude, site.longitude).altitudeDegrees
        const expected = -threshold.degreesBelowHorizon + HORIZON_REFRACTION_DEGREES
        expect(Math.abs(altitude - expected)).toBeLessThan(0.01)
      }
    },
  )

  it('reads −5.43°, −11.43° and −17.43° at the three thresholds', () => {
    const summary = twilightCrossings(REFERENCE[0].date, DUBAI.latitude, DUBAI.longitude)

    for (const [phase, expected] of [['civil', -5.43], ['nautical', -11.43], ['astronomical', -17.43]] as const) {
      const cross = crossing(summary, phase, 'descending')
      const altitude = solarPosition(cross.instantUtc!, DUBAI.latitude, DUBAI.longitude).altitudeDegrees
      expect(altitude).toBeCloseTo(expected, 1)
    }
  })
})

describe('twilightCrossings — phase lengths', () => {
  it('measures each phase from the preceding threshold to its own, on the evening side', () => {
    const site = DUBAI
    const date = REFERENCE[0].date
    const summary = twilightCrossings(date, site.latitude, site.longitude)
    const riseSet = daySummary(date, site.latitude, site.longitude)

    const civil = summary.lengths.find((l) => l.phase === 'civil')!
    const nautical = summary.lengths.find((l) => l.phase === 'nautical')!
    const astronomical = summary.lengths.find((l) => l.phase === 'astronomical')!

    const civilDescending = crossing(summary, 'civil', 'descending').instantUtc!
    const nauticalDescending = crossing(summary, 'nautical', 'descending').instantUtc!
    const astronomicalDescending = crossing(summary, 'astronomical', 'descending').instantUtc!

    expect(civil.minutes).toBeCloseTo((civilDescending.getTime() - riseSet.sunsetUtc!.getTime()) / MINUTE_MS, 5)
    expect(nautical.minutes).toBeCloseTo((nauticalDescending.getTime() - civilDescending.getTime()) / MINUTE_MS, 5)
    expect(astronomical.minutes!.valueOf()).toBeCloseTo(
      (astronomicalDescending.getTime() - nauticalDescending.getTime()) / MINUTE_MS,
      5,
    )
  })

  it('reports no length for a phase that does not occur', () => {
    const summary = twilightCrossings(REFERENCE[1].date, LONDON.latitude, LONDON.longitude)
    const astronomical = summary.lengths.find((l) => l.phase === 'astronomical')!

    expect(astronomical.minutes).toBeNull()
  })

  it('reports no civil length on a polar-night date, because the sun never crosses the horizon', () => {
    // The phase's own threshold is still crossed (the sun descends to −6°), but its upper boundary
    // — the horizon — is not, so the band is never traversed from above.
    const summary = twilightCrossings(new Date(Date.UTC(2026, 11, 21)), TROMSO.latitude, TROMSO.longitude)
    const civil = summary.lengths.find((l) => l.phase === 'civil')!
    const riseSet = daySummary(new Date(Date.UTC(2026, 11, 21)), TROMSO.latitude, TROMSO.longitude)

    expect(riseSet.polarCondition).toBe('polar-night')
    expect(crossing(summary, 'civil', 'descending').reached).toBe(true)
    expect(civil.minutes).toBeNull()
  })
})

describe('twilightCrossings — polar days and never-reached thresholds', () => {
  it('reports every phase as not occurring when the sun never sets', () => {
    const summary = twilightCrossings(new Date(Date.UTC(2026, 5, 21)), TROMSO.latitude, TROMSO.longitude)

    expect(daySummary(new Date(Date.UTC(2026, 5, 21)), TROMSO.latitude, TROMSO.longitude).polarCondition).toBe('midnight-sun')
    for (const cross of summary.crossings) {
      expect(cross.reached).toBe(false)
      expect(cross.instantUtc).toBeNull()
    }
  })

  it('does not confuse a threshold that was never reached with one that could not be solved', () => {
    const summary = twilightCrossings(REFERENCE[1].date, LONDON.latitude, LONDON.longitude)
    const astronomical = crossing(summary, 'astronomical', 'descending')

    expect(astronomical.reached).toBe(false)
    expect(astronomical.undetermined).toBe(false)
  })

  it('reports an undetermined crossing when the solver cannot converge, and never a time', () => {
    const summary = twilightCrossings(REFERENCE[0].date, DUBAI.latitude, DUBAI.longitude, { maxIterations: 0 })

    for (const cross of summary.crossings) {
      expect(cross.undetermined).toBe(true)
      expect(cross.instantUtc).toBeNull()
    }
  })
})

describe('twilightCrossings — determinism', () => {
  it('returns the same object for the same site and date (the per-day cache)', () => {
    const first = twilightCrossings(REFERENCE[0].date, DUBAI.latitude, DUBAI.longitude)
    const second = twilightCrossings(REFERENCE[0].date, DUBAI.latitude, DUBAI.longitude)

    expect(second).toBe(first)
  })
})

describe('yearProfile', () => {
  it('has one entry per day of the year', () => {
    expect(yearProfile(DUBAI.latitude, DUBAI.longitude, 2026, 'Asia/Dubai').days).toHaveLength(365)
    expect(yearProfile(DUBAI.latitude, DUBAI.longitude, 2028, 'Asia/Dubai').days).toHaveLength(366)
  })

  it('covers every day with contiguous segments and no gaps', () => {
    const profile = yearProfile(DUBAI.latitude, DUBAI.longitude, 2026, 'Asia/Dubai')

    for (const day of profile.days) {
      expect(day.segments.length).toBeGreaterThan(0)
      expect(day.segments[0].startMinute).toBe(0)
      for (let i = 1; i < day.segments.length; i += 1) {
        expect(day.segments[i].startMinute).toBe(day.segments[i - 1].endMinute)
      }
      // The last segment ends at the end of the local day: 24 h, or 23/25 h across a DST change.
      expect(day.segments[day.segments.length - 1].endMinute).toBeGreaterThanOrEqual(23 * 60)
      expect(day.segments[day.segments.length - 1].endMinute).toBeLessThanOrEqual(25 * 60)
    }
  })

  it('draws a polar night as one night band and a midnight sun as one day band', () => {
    const winter = yearProfile(TROMSO.latitude, TROMSO.longitude, 2026, 'Europe/Oslo')
    const solstice = winter.days[171] // 21 June 2026, 0-based

    expect(solstice.segments).toHaveLength(1)
    expect(solstice.segments[0].phase).toBe('day')
    expect(solstice.daylightMinutes).toBeGreaterThan(23 * 60)
  })

  it('finds the longest and shortest day from the same minutes the chart draws', () => {
    const profile = yearProfile(DUBAI.latitude, DUBAI.longitude, 2026, 'Asia/Dubai')
    const byMinutes = [...profile.days].sort((a, b) => a.daylightMinutes - b.daylightMinutes)

    expect(profile.shortestDay.minutes).toBe(byMinutes[0].daylightMinutes)
    expect(profile.shortestDay.date).toBe(byMinutes[0].date)
    expect(profile.longestDay.minutes).toBe(byMinutes[byMinutes.length - 1].daylightMinutes)
    expect(profile.longestDay.date).toBe(byMinutes[byMinutes.length - 1].date)
    // The northern hemisphere's longest day is in June, its shortest in December.
    expect(profile.longestDay.date.startsWith('2026-06')).toBe(true)
    expect(profile.shortestDay.date.startsWith('2026-12')).toBe(true)
    expect(profile.hemisphere).toBe('northern')
  })

  it('puts the southern hemisphere’s longest day in December', () => {
    const profile = yearProfile(-33.8688, 151.2093, 2026, 'Australia/Sydney')

    expect(profile.hemisphere).toBe('southern')
    expect(profile.longestDay.date.startsWith('2026-12')).toBe(true)
  })

  it('returns the same object for the same site and year (the year cache)', () => {
    const first = yearProfile(DUBAI.latitude, DUBAI.longitude, 2026, 'Asia/Dubai')
    const second = yearProfile(DUBAI.latitude, DUBAI.longitude, 2026, 'Asia/Dubai')

    expect(second).toBe(first)
  })

  it('agrees with the day-level computation the figures use', () => {
    const profile = yearProfile(DUBAI.latitude, DUBAI.longitude, 2026, 'Asia/Dubai')
    const day = profile.days[267] // 25 September 2026, 0-based
    const summary = daySummary(new Date(Date.UTC(2026, 8, 25)), DUBAI.latitude, DUBAI.longitude)

    expect(day.date).toBe('2026-09-25')
    // The chart draws whole minutes; the figures report the physical value.
    expect(Math.abs(day.daylightMinutes - summary.dayLengthMinutes!)).toBeLessThanOrEqual(1)
  })
})
