import { describe, expect, it } from 'vitest'
import { panelContentSchema } from '../../../viewer/panels/content/blocks'
import { copy } from '../copy'
import { daySummary } from '../solar/daySummary'
import { SOLAR_SEMIDIAMETER_DEGREES } from '../solar/refraction'
import { MIN_SHADOW_ELEVATION_DEGREES } from '../scene/sunLight'
import { SOLAR_POSITION_TOLERANCE_DEGREES, solarPosition } from '../solar/solarPosition'
import { twilightCrossings } from '../solar/twilight'
import { buildSolarFiguresContent } from './solarFiguresContent'

const DUBAI = { latitude: 25.2, longitude: 55.3 }

function buildFor(instantUtc: Date, localDate: string, localMinuteOfDay: number) {
  return buildSolarFiguresContent({
    localDate,
    localMinuteOfDay,
    timeZoneId: 'Asia/Dubai',
    timeBasisLabel: 'Asia/Dubai',
    solarPosition: solarPosition(instantUtc, DUBAI.latitude, DUBAI.longitude),
    daySummary: daySummary(instantUtc, DUBAI.latitude, DUBAI.longitude),
    twilight: twilightCrossings(instantUtc, DUBAI.latitude, DUBAI.longitude),
    siteBuildingHeightAssumed: true,
  })
}

describe('buildSolarFiguresContent (contracts/solar-panels.md, FR-031)', () => {
  it('validates against panelContentSchema', () => {
    const content = buildFor(new Date(Date.UTC(2026, 8, 13, 10, 0)), '2026-09-13', 14 * 60)
    expect(() => panelContentSchema.parse(content)).not.toThrow()
  })

  it('always carries a "Times shown in" row (FR-003)', () => {
    const content = buildFor(new Date(Date.UTC(2026, 8, 13, 10, 0)), '2026-09-13', 14 * 60)
    const keyValueBlock = content.blocks.find((b) => (b as { kind: string }).kind === 'keyValue') as unknown as {
      items: { label: string; value: unknown }[]
    }
    const timesRow = keyValueBlock.items.find((i) => i.label === copy.timesShownInLabel)
    expect(timesRow).toBeDefined()
    expect(timesRow!.value).toBe('Asia/Dubai')
  })

  it('always carries the design-stage-study / accuracy / assumed-heights statement (FR-043, FR-044, SC-010)', () => {
    const content = buildFor(new Date(Date.UTC(2026, 8, 13, 10, 0)), '2026-09-13', 14 * 60)
    const textBlock = content.blocks.find((b) => (b as { kind: string }).kind === 'text') as unknown as { text: string }
    expect(textBlock.text).toContain(copy.designStageStudyStatement)
    expect(textBlock.text).toContain('accurate to within')
    expect(textBlock.text).toContain(copy.assumedHeightsStatement)
  })

  it('replaces the sunrise row with the plain polar statement rather than a blank (FR-002)', () => {
    // Tromsø-like coordinates in midnight sun — reuse Dubai's builder shape but with a polar site.
    const content = buildSolarFiguresContent({
      localDate: '2026-06-21',
      localMinuteOfDay: 12 * 60,
      timeZoneId: 'Europe/Oslo',
      timeBasisLabel: 'Europe/Oslo',
      solarPosition: solarPosition(new Date(Date.UTC(2026, 5, 21, 12, 0)), 69.6, 18.9),
      daySummary: daySummary(new Date(Date.UTC(2026, 5, 21)), 69.6, 18.9),
      twilight: twilightCrossings(new Date(Date.UTC(2026, 5, 21)), 69.6, 18.9),
      siteBuildingHeightAssumed: null,
    })
    const keyValueBlock = content.blocks.find((b) => (b as { kind: string }).kind === 'keyValue') as unknown as {
      items: { label: string; value: unknown }[]
    }
    const sunsetRow = keyValueBlock.items.find((i) => i.label === copy.sunsetLabel)
    expect(sunsetRow).toBeDefined()
    expect(sunsetRow!.value).toBe(copy.sunNeverSets)
    // Never a blank or null rendered as a dash.
    expect(sunsetRow!.value).not.toBeNull()
    expect(sunsetRow!.value).not.toBe('')
  })

  it('adds the below-horizon statement when the sun is below the horizon (FR-017)', () => {
    const content = buildFor(new Date(Date.UTC(2026, 8, 13, 0, 0)), '2026-09-13', 4 * 60) // deep night
    const textBlock = content.blocks.find((b) => (b as { kind: string }).kind === 'text') as unknown as { text: string }
    expect(textBlock.text).toContain(copy.belowHorizonNotice)
  })

  it('does not add the below-horizon statement when the sun is above the horizon', () => {
    const content = buildFor(new Date(Date.UTC(2026, 8, 13, 10, 0)), '2026-09-13', 14 * 60) // midday
    const textBlock = content.blocks.find((b) => (b as { kind: string }).kind === 'text') as unknown as { text: string }
    expect(textBlock.text).not.toContain(copy.belowHorizonNotice)
  })
})

describe('buildSolarFiguresContent — the altitude quantity is named (T016, FR-003)', () => {
  const closingTextOf = (content: ReturnType<typeof buildFor>): string =>
    (content.blocks.find((b) => (b as { kind: string }).kind === 'text') as unknown as { text: string }).text

  it('states that the altitude shown is the refraction-corrected centre of the sun', () => {
    const content = buildFor(new Date(Date.UTC(2026, 8, 13, 10, 0)), '2026-09-13', 14 * 60)
    expect(closingTextOf(content)).toContain(copy.altitudeQuantityStatement)
  })

  it('names it whatever the sun is doing — a user checking against a reference needs it at any hour', () => {
    for (const hour of [0, 3, 6, 12, 18, 23]) {
      const content = buildFor(new Date(Date.UTC(2026, 8, 13, hour, 0)), '2026-09-13', hour * 60)
      expect(closingTextOf(content)).toContain(copy.altitudeQuantityStatement)
    }
  })

  it('reads the accuracy figure from SOLAR_POSITION_TOLERANCE_DEGREES rather than a literal', () => {
    // T014 re-measures that constant. This asserts the panel's wording follows it automatically:
    // the expected string is *built from* the constant, so a hand-typed figure in copy.ts or a
    // stale number in the panel fails here rather than shipping a claim the tests do not check.
    const content = buildFor(new Date(Date.UTC(2026, 8, 13, 10, 0)), '2026-09-13', 14 * 60)
    expect(closingTextOf(content)).toContain(copy.accuracyStatement(SOLAR_POSITION_TOLERANCE_DEGREES))
    expect(copy.accuracyStatement(SOLAR_POSITION_TOLERANCE_DEGREES)).toContain(String(SOLAR_POSITION_TOLERANCE_DEGREES))
  })
})

describe('buildSolarFiguresContent — no below-horizon notice at the reported sunrise (T017)', () => {
  /**
   * The exact contradiction captured in baseline.md: at the sunrise the panel itself reported, the
   * same panel also said the sun was below the horizon. It is pinned here at the reported *minute*,
   * not the solved millisecond, because the displayed minute is what a user can act on — it is the
   * value they read off the screen and type back into the time field.
   */
  const SITES = [
    { name: 'Dubai', latitude: 25.2, longitude: 55.3, timeZoneId: 'Asia/Dubai' },
    { name: 'London', latitude: 51.5, longitude: -0.1, timeZoneId: 'Europe/London' },
    { name: 'Reykjavík', latitude: 64.13, longitude: -21.9, timeZoneId: 'Atlantic/Reykjavik' },
  ] as const

  const closingTextAt = (instantUtc: Date, site: (typeof SITES)[number]): string => {
    const content = buildSolarFiguresContent({
      localDate: '2026-09-21',
      localMinuteOfDay: 6 * 60,
      timeZoneId: site.timeZoneId,
      timeBasisLabel: site.timeZoneId,
      solarPosition: solarPosition(instantUtc, site.latitude, site.longitude),
      daySummary: daySummary(instantUtc, site.latitude, site.longitude),
      twilight: twilightCrossings(instantUtc, site.latitude, site.longitude),
      siteBuildingHeightAssumed: null,
    })
    return (content.blocks.find((b) => (b as { kind: string }).kind === 'text') as unknown as { text: string }).text
  }

  for (const site of SITES) {
    for (const month of [2, 5, 8, 11]) {
      it(`does not claim the sun is below the horizon at ${site.name}'s own reported sunrise minute (month ${month + 1})`, () => {
        const summary = daySummary(new Date(Date.UTC(2026, month, 21)), site.latitude, site.longitude)
        if (summary.polarCondition !== 'none') return

        const reportedMinute = new Date(Math.ceil(summary.sunriseUtc!.getTime() / 60_000) * 60_000)
        expect(closingTextAt(reportedMinute, site)).not.toContain(copy.belowHorizonNotice)
      })
    }
  }

  it('still says so at night, when the sun really is below the horizon', () => {
    const midnight = new Date(Date.UTC(2026, 8, 21, 20, 0))
    expect(closingTextAt(midnight, SITES[0])).toContain(copy.belowHorizonNotice)
  })

  it('still says so an hour before sunrise — the notice is narrowed, not removed', () => {
    const summary = daySummary(new Date(Date.UTC(2026, 8, 21)), SITES[0].latitude, SITES[0].longitude)
    const anHourEarlier = new Date(summary.sunriseUtc!.getTime() - 60 * 60_000)
    expect(closingTextAt(anHourEarlier, SITES[0])).toContain(copy.belowHorizonNotice)
  })

  /**
   * The notice that replaces the below-horizon one has to be readable beside the number printed
   * next to it. At the reported sunrise the upper edge is up but the *centre* — the altitude the
   * panel shows — is one solar radius down, so the notice is on screen next to a negative
   * reading. Its earlier wording, "less than 1° above the horizon", contradicted that reading on
   * sight. Nothing pinned the low-sun notice at all before this, in either sign.
   */
  for (const site of SITES) {
    it(`states the low-sun reason, not a contradiction, at ${site.name}'s reported sunrise minute`, () => {
      const summary = daySummary(new Date(Date.UTC(2026, 8, 21)), site.latitude, site.longitude)
      if (summary.polarCondition !== 'none') return

      const reportedMinute = new Date(Math.ceil(summary.sunriseUtc!.getTime() / 60_000) * 60_000)
      const altitude = solarPosition(reportedMinute, site.latitude, site.longitude).altitudeDegrees
      const closing = closingTextAt(reportedMinute, site)

      // The premise: the panel really is printing a sub-zero altitude here.
      expect(altitude).toBeLessThan(0)
      expect(altitude).toBeGreaterThan(-SOLAR_SEMIDIAMETER_DEGREES)

      expect(closing).toContain(copy.lowSunNoShadowsNotice(MIN_SHADOW_ELEVATION_DEGREES))
      // Nothing in it may claim the sun is *above* the horizon while that number is negative.
      expect(closing).not.toMatch(/above the horizon/)
    })
  }

  it('states the same reason once the sun is genuinely above the horizon but under the threshold', () => {
    const site = SITES[0]
    const summary = daySummary(new Date(Date.UTC(2026, 8, 21)), site.latitude, site.longitude)
    const twoMinutesLater = new Date(summary.sunriseUtc!.getTime() + 2 * 60_000)
    const altitude = solarPosition(twoMinutesLater, site.latitude, site.longitude).altitudeDegrees

    expect(altitude).toBeGreaterThan(0)
    expect(altitude).toBeLessThan(MIN_SHADOW_ELEVATION_DEGREES)
    expect(closingTextAt(twoMinutesLater, site)).toContain(
      copy.lowSunNoShadowsNotice(MIN_SHADOW_ELEVATION_DEGREES),
    )
  })
})

describe('buildSolarFiguresContent — the sunrise, sunset and day-length rows agree', () => {
  // Found by verifying specs/063 against the running app at Badr, Egypt: the panel printed
  // sunrise 06:42, sunset 18:50 and a day length of "12 h 9 min" — a user subtracting the two
  // clock times printed directly above got 12 h 8 min instead. The times were rounded (sunrise
  // up, sunset down) while the length came from the unrounded instants, so the length could run
  // up to two minutes past the interval shown. Measured against the old code, the two disagreed
  // on 310 of 365 days at Badr — the normal case, not a rare rounding edge. Same class of
  // self-contradiction specs/063 was written to remove, so it is pinned here across a full year
  // and a range of latitudes.
  const SITES = [
    { name: 'Badr', latitude: 30.1287, longitude: 31.722, timeZoneId: 'Africa/Cairo' },
    { name: 'Dubai', latitude: 25.2, longitude: 55.3, timeZoneId: 'Asia/Dubai' },
    { name: 'London', latitude: 51.5, longitude: -0.1, timeZoneId: 'Europe/London' },
  ] as const

  const rowsFor = (instantUtc: Date, site: (typeof SITES)[number]) => {
    const content = buildSolarFiguresContent({
      localDate: instantUtc.toISOString().slice(0, 10),
      localMinuteOfDay: 12 * 60,
      timeZoneId: site.timeZoneId,
      timeBasisLabel: site.timeZoneId,
      solarPosition: solarPosition(instantUtc, site.latitude, site.longitude),
      daySummary: daySummary(instantUtc, site.latitude, site.longitude),
      twilight: twilightCrossings(instantUtc, site.latitude, site.longitude),
      siteBuildingHeightAssumed: null,
    })
    const block = content.blocks.find((b) => (b as { kind: string }).kind === 'keyValue') as unknown as {
      items: { label: string; value: string }[]
    }
    const valueOf = (label: string) => block.items.find((i) => i.label === label)?.value
    return {
      sunrise: valueOf(copy.sunriseLabel),
      sunset: valueOf(copy.sunsetLabel),
      dayLength: valueOf(copy.dayLengthLabel),
    }
  }

  const clockToMinutes = (clock: string): number => {
    const [hours, minutes] = clock.split(':').map(Number)
    return hours * 60 + minutes
  }

  for (const site of SITES) {
    it(`prints a day length equal to sunset minus sunrise, every day of the year, at ${site.name}`, () => {
      let checkedDays = 0
      for (let dayOfYear = 0; dayOfYear < 365; dayOfYear += 1) {
        const noonUtc = new Date(Date.UTC(2026, 0, 1 + dayOfYear, 12))
        const { sunrise, sunset, dayLength } = rowsFor(noonUtc, site)
        // Polar and unconverged days print prose in place of a time; nothing to reconcile there.
        if (!sunrise?.includes(':') || !sunset?.includes(':')) continue

        const match = /^(\d+) h (\d+) min$/.exec(dayLength!)
        expect(match, `unparsable day length "${dayLength}" on day ${dayOfYear}`).not.toBeNull()
        const printedLength = Number(match![1]) * 60 + Number(match![2])

        expect(printedLength, `${site.name} day ${dayOfYear}: ${sunrise}–${sunset} vs ${dayLength}`).toBe(
          clockToMinutes(sunset) - clockToMinutes(sunrise),
        )
        checkedDays += 1
      }
      // Guards against the loop silently skipping everything and passing vacuously.
      expect(checkedDays).toBeGreaterThan(300)
    })
  }

  it('reproduces the Badr case from the specs/063 verification run', () => {
    const { sunrise, sunset, dayLength } = rowsFor(new Date(Date.UTC(2026, 8, 22, 12)), SITES[0])
    expect(clockToMinutes(sunset!) - clockToMinutes(sunrise!)).toBe(
      Number(/^(\d+) h/.exec(dayLength!)![1]) * 60 + Number(/(\d+) min$/.exec(dayLength!)![1]),
    )
  })
})

// specs/085 FR-001 … FR-008 — the twilight figures beside the rise/set rows they continue.
describe('twilight rows (specs/085)', () => {
  const rowsFor = (latitude: number, longitude: number, date: string, timeZoneId: string) => {
    const [year, month, day] = date.split('-').map(Number)
    const dateUtc = new Date(Date.UTC(year, month - 1, day))
    const content = buildSolarFiguresContent({
      localDate: date,
      localMinuteOfDay: 12 * 60,
      timeZoneId,
      timeBasisLabel: timeZoneId,
      solarPosition: solarPosition(new Date(dateUtc.getTime() + 12 * 3_600_000), latitude, longitude),
      daySummary: daySummary(dateUtc, latitude, longitude),
      twilight: twilightCrossings(dateUtc, latitude, longitude),
      siteBuildingHeightAssumed: null,
    })
    const keyValue = content.blocks.find((block) => block.kind === 'keyValue') as unknown as {
      items: { label: string; value: string }[]
    }
    const text = content.blocks.find((block) => block.kind === 'text') as unknown as { text: string }
    return { items: keyValue.items, text: text.text }
  }

  it('reports the three phases in order, after the day length and before the time basis', () => {
    const { items } = rowsFor(25.2, 55.3, '2026-09-13', 'Asia/Dubai')
    const labels = items.map((item) => item.label)

    const dayLength = labels.indexOf(copy.dayLengthLabel)
    const civil = labels.indexOf(copy.civilTwilightLabel)
    const nautical = labels.indexOf(copy.nauticalTwilightLabel)
    const astronomical = labels.indexOf(copy.astronomicalTwilightLabel)
    const timeBasis = labels.indexOf(copy.timesShownInLabel)

    expect(dayLength).toBeGreaterThanOrEqual(0)
    expect(civil).toBe(dayLength + 1)
    expect(nautical).toBe(civil + 1)
    expect(astronomical).toBe(nautical + 1)
    expect(timeBasis).toBe(astronomical + 1)
  })

  it('gives each phase both crossings and the length of its band (FR-001, FR-002)', () => {
    const { items } = rowsFor(25.2, 55.3, '2026-09-13', 'Asia/Dubai')

    for (const label of [copy.civilTwilightLabel, copy.nauticalTwilightLabel, copy.astronomicalTwilightLabel]) {
      const row = items.find((item) => item.label === label)!
      expect(row.value).toMatch(/^ends \d{2}:\d{2} · begins \d{2}:\d{2} · \d+ min$/)
    }
  })

  it('says a phase does not occur, naming the threshold, with no time shown (FR-006)', () => {
    // London in midsummer never reaches 18° below the horizon.
    const { items } = rowsFor(51.5, -0.1, '2026-06-21', 'Europe/London')
    const astronomical = items.find((item) => item.label === copy.astronomicalTwilightLabel)!

    expect(astronomical.value).toBe(copy.twilightNotReached(18))
    expect(astronomical.value).not.toMatch(/\d{2}:\d{2}/)
  })

  it('keeps saying the sun does not set where it always has, and reports every phase as not occurring', () => {
    // Tromsø in midsummer: the sun never descends to 6°, so no twilight phase occurs — and the
    // polar statement stays the only place that says the sun does not set (research D6).
    const { items } = rowsFor(69.6, 18.9, '2026-06-21', 'Europe/Oslo')

    expect(items.find((item) => item.label === copy.sunsetLabel)!.value).toBe(copy.sunNeverSets)
    expect(items.find((item) => item.label === copy.civilTwilightLabel)!.value).toBe(copy.twilightNotReached(6))
    expect(items.find((item) => item.label === copy.nauticalTwilightLabel)!.value).toBe(copy.twilightNotReached(12))
    expect(items.find((item) => item.label === copy.astronomicalTwilightLabel)!.value).toBe(copy.twilightNotReached(18))
  })

  it('states that the thresholds are unrefracted, with the constants the altitude figure reads (FR-004, FR-005)', () => {
    const { text } = rowsFor(25.2, 55.3, '2026-09-13', 'Asia/Dubai')

    expect(text).toContain(copy.twilightDefinitionStatement(-5.43, -11.43, -17.43))
  })

  it('leaves the existing rise/set figures byte-for-byte unchanged (SC-005)', () => {
    const { items } = rowsFor(25.2, 55.3, '2026-09-13', 'Asia/Dubai')
    const sunrise = items.find((item) => item.label === copy.sunriseLabel)!
    const sunset = items.find((item) => item.label === copy.sunsetLabel)!
    const dayLength = items.find((item) => item.label === copy.dayLengthLabel)!

    expect(sunrise.value).toMatch(/^\d{2}:\d{2}$/)
    expect(sunset.value).toMatch(/^\d{2}:\d{2}$/)
    expect(dayLength.value).toMatch(/^\d+ h \d+ min$/)
  })
})
