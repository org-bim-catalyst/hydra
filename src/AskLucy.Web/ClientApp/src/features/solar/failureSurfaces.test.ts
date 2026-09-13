import { describe, expect, it } from 'vitest'
import { copy } from './copy'
import { buildSolarFiguresContent } from './panels/solarFiguresContent'
import { validateGroundOffset, validateHeight } from './store/correctionsStore'
import { daySummary } from './solar/daySummary'
import { solarPosition } from './solar/solarPosition'
import { FALLBACK_TIME_ZONE, resolveTimeZone } from './solar/timeZone'

/**
 * T071, SC-008, FR-045, FR-046, constitution §2.VIII — consolidated sweep over quickstart.md
 * Scenario 7's forced-failure table. Each row not already the dedicated subject of its own test
 * file (`SolarAnalysisOverlay.test.tsx` for the Overpass/no-buildings notices,
 * `correctionsStore.test.ts`/`BuildingCorrectionsPanel.test.tsx` for invalid input,
 * `OpenSolarAnalysisCapabilityTests.cs` for "no active site") is asserted here directly, so this
 * one file is the place that maps every row in that table to the test that actually proves it —
 * and fills the one row not otherwise covered: the undetermined-time-zone case reaching the
 * figures document itself, not just `timeZone.ts` in isolation.
 *
 * Two rows are framework-owned and are NOT re-tested here, by design: "WebGL unsupported" is the
 * viewer's existing unsupported-3D notice (specs/049-051's own test suites), and "frame callback
 * throws" is `DrawingSpaceRegistry.invokeFrameCallbacks`'s existing try/catch containment
 * (specs/051) — this feature's `onFrame` callback deliberately adds no `try/catch` of its own,
 * which is correct per that framework contract, not a gap.
 */
describe('Failure-surface sweep (quickstart Scenario 7) — nothing is observable only in logs', () => {
  it('Overpass 503 / zero buildings notices are non-empty, stated strings (see SolarAnalysisOverlay.test.tsx for the end-to-end path)', () => {
    expect(copy.buildingDataUnavailable).toBeTruthy()
    expect(copy.noBuildingsFound).toBeTruthy()
  })

  it('time zone undetermined: the figures document states UTC explicitly, never a bare zone id that reads as determined', async () => {
    const basis = await resolveTimeZone(Number.NaN, 0) // forces the undetermined path
    expect(basis.timeZoneId).toBeNull()

    const instant = new Date(Date.UTC(2026, 5, 21, 12, 0))
    const content = buildSolarFiguresContent({
      localDate: '2026-06-21',
      localMinuteOfDay: 12 * 60,
      timeZoneId: basis.timeZoneId,
      timeBasisLabel: basis.timeBasisLabel,
      solarPosition: solarPosition(instant, 51.5, -0.1),
      daySummary: daySummary(instant, 51.5, -0.1),
      siteBuildingHeightAssumed: null,
    })

    const keyValueBlock = content.blocks.find((b) => (b as { kind: string }).kind === 'keyValue') as unknown as {
      items: { label: string; value: unknown }[]
    }
    const timesRow = keyValueBlock.items.find((i) => i.label === copy.timesShownInLabel)
    expect(timesRow!.value).toBe(copy.timeBasisUndetermined)
    expect(String(timesRow!.value)).not.toBe(FALLBACK_TIME_ZONE) // never a bare "UTC" that reads as determined
  })

  it('invalid height/offset are rejected with a non-empty stated reason (see correctionsStore.test.ts for the full matrix)', () => {
    expect(validateHeight(-1).ok).toBe(false)
    expect(validateHeight(-1).reason).toBeTruthy()
    expect(validateGroundOffset(9999).ok).toBe(false)
    expect(validateGroundOffset(9999).reason).toBeTruthy()
  })

  it('Lucy-no-active-site and viewer-unavailable refusal reasons are non-empty stated strings (see OpenSolarAnalysisCapabilityTests.cs)', () => {
    expect(copy.noActiveSite).toBeTruthy()
    expect(copy.viewerUnavailable).toBeTruthy()
  })

  it('every copy.ts string used as a failure/notice surface is non-empty (a blank string would be a silent failure by another name)', () => {
    const failureStrings = [
      copy.timeBasisUndetermined,
      copy.sunNeverRises,
      copy.sunNeverSets,
      copy.belowHorizonNotice,
      copy.noBuildingsFound,
      copy.buildingDataUnavailable,
      copy.invalidHeight,
      copy.invalidGroundOffset,
      copy.noActiveSite,
      copy.viewerUnavailable,
      copy.webglUnsupported,
      copy.extensionFailure,
      copy.noSiteBuildingFound,
    ]
    for (const value of failureStrings) {
      expect(value.trim().length).toBeGreaterThan(0)
    }
  })
})
