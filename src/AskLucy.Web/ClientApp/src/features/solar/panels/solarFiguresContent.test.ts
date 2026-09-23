import { describe, expect, it } from 'vitest'
import { panelContentSchema } from '../../../viewer/panels/content/blocks'
import { copy } from '../copy'
import { daySummary } from '../solar/daySummary'
import { SOLAR_SEMIDIAMETER_DEGREES } from '../solar/refraction'
import { MIN_SHADOW_ELEVATION_DEGREES } from '../scene/sunLight'
import { SOLAR_POSITION_TOLERANCE_DEGREES, solarPosition } from '../solar/solarPosition'
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
