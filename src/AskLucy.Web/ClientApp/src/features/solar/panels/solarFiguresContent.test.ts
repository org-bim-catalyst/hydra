import { describe, expect, it } from 'vitest'
import { panelContentSchema } from '../../../viewer/panels/content/blocks'
import { copy } from '../copy'
import { daySummary } from '../solar/daySummary'
import { solarPosition } from '../solar/solarPosition'
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
