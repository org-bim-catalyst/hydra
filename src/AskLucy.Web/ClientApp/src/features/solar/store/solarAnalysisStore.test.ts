import { beforeEach, describe, expect, it } from 'vitest'
import { copy } from '../copy'
import { solarFailureCopy, useSolarAnalysisStore } from './solarAnalysisStore'

beforeEach(() => {
  useSolarAnalysisStore.setState({
    site: null,
    moment: null,
    status: 'idle',
    failureReason: null,
    buildingsNotice: null,
  })
})

describe('solarAnalysisStore — instantUtc is canonical (FR-004)', () => {
  it('editing the local date recomputes instantUtc, and the projection reads back the same date', async () => {
    await useSolarAnalysisStore.getState().open(25.2, 55.3) // Dubai, UTC+4, no DST
    useSolarAnalysisStore.getState().setLocalMinuteOfDay(14 * 60) // 14:00 local
    useSolarAnalysisStore.getState().setLocalDate('2026-06-21')

    const { moment } = useSolarAnalysisStore.getState()
    expect(moment!.localDate).toBe('2026-06-21')
    expect(moment!.localMinuteOfDay).toBe(14 * 60)
    // 14:00 Asia/Dubai (UTC+4) == 10:00 UTC
    expect(moment!.instantUtc.getUTCHours()).toBe(10)
  })

  it('editing the local minute-of-day recomputes instantUtc consistently', async () => {
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
    useSolarAnalysisStore.getState().setLocalDate('2026-06-21')
    useSolarAnalysisStore.getState().setLocalMinuteOfDay(9 * 60 + 30)

    const { moment } = useSolarAnalysisStore.getState()
    expect(moment!.localMinuteOfDay).toBe(9 * 60 + 30)
    expect(moment!.instantUtc.getUTCHours()).toBe(5)
    expect(moment!.instantUtc.getUTCMinutes()).toBe(30)
  })

  it('setInstantUtc re-derives both local projections from the one canonical value', async () => {
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
    const instant = new Date(Date.UTC(2026, 0, 1, 10, 0))
    useSolarAnalysisStore.getState().setInstantUtc(instant)

    const { moment } = useSolarAnalysisStore.getState()
    expect(moment!.instantUtc).toBe(instant)
    expect(moment!.localDate).toBe('2026-01-01')
    expect(moment!.localMinuteOfDay).toBe(14 * 60) // 10:00 UTC + 4h
  })
})

describe('solarAnalysisStore — status lifecycle (data-model.md)', () => {
  it('"partial" is not a failure state — it always carries a buildings notice, never a failureReason', async () => {
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
    useSolarAnalysisStore.getState().markPartial(copy.noBuildingsFound)

    const state = useSolarAnalysisStore.getState()
    expect(state.status).toBe('partial')
    expect(state.failureReason).toBeNull()
    expect(state.buildingsNotice).toBe(copy.noBuildingsFound)
  })

  it('every transition into "failed" sets a user-facing string drawn from copy.ts (FR-045, SC-008)', async () => {
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
    useSolarAnalysisStore.getState().markFailed(solarFailureCopy.viewerUnavailable)

    const state = useSolarAnalysisStore.getState()
    expect(state.status).toBe('failed')
    expect(state.failureReason).toBe(copy.viewerUnavailable)
    expect(state.failureReason).not.toBe('')
  })

  it('close() returns to idle and clears every field (FR-024, FR-040)', async () => {
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
    useSolarAnalysisStore.getState().markFailed('some failure')
    useSolarAnalysisStore.getState().close()

    const state = useSolarAnalysisStore.getState()
    expect(state.status).toBe('idle')
    expect(state.site).toBeNull()
    expect(state.moment).toBeNull()
    expect(state.failureReason).toBeNull()
  })
})

describe('solarAnalysisStore — playback (US3)', () => {
  it('advanceBy moves instantUtc forward by deltaSeconds * playbackMinutesPerSecond', async () => {
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
    useSolarAnalysisStore.getState().setInstantUtc(new Date(Date.UTC(2026, 0, 1, 0, 0)))
    useSolarAnalysisStore.getState().setPlaybackMinutesPerSecond(120)
    useSolarAnalysisStore.getState().advanceBy(1) // 1 real second -> 120 local minutes

    const { moment } = useSolarAnalysisStore.getState()
    expect(moment!.instantUtc.getTime()).toBe(Date.UTC(2026, 0, 1, 2, 0))
  })

  it('stopping playback leaves the moment where it stopped (FR-021)', async () => {
    await useSolarAnalysisStore.getState().open(25.2, 55.3)
    useSolarAnalysisStore.getState().setPlaying(true)
    useSolarAnalysisStore.getState().advanceBy(2)
    const beforeStop = useSolarAnalysisStore.getState().moment!.instantUtc.getTime()
    useSolarAnalysisStore.getState().setPlaying(false)

    const { moment } = useSolarAnalysisStore.getState()
    expect(moment!.isPlaying).toBe(false)
    expect(moment!.instantUtc.getTime()).toBe(beforeStop)
  })
})
