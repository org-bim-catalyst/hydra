import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'
import { SolarTimeControlPanel } from './SolarTimeControlPanel'

describe('SolarTimeControlPanel (contracts/solar-panels.md "Time Control")', () => {
  beforeEach(async () => {
    useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null })
    await useSolarAnalysisStore.getState().open(25.2, 55.3) // Asia/Dubai, UTC+4, no DST
    useSolarAnalysisStore.getState().setInstantUtc(new Date(Date.UTC(2026, 5, 21, 10, 0))) // 14:00 local
  })

  it('renders nothing before a moment exists', () => {
    useSolarAnalysisStore.setState({ moment: null })
    const { container } = render(<SolarTimeControlPanel />)
    expect(container.firstChild).toBeNull()
  })

  it("the slider's aria-valuetext carries the local time, not the raw minute count (FR-032)", () => {
    render(<SolarTimeControlPanel />)
    const slider = screen.getByRole('slider', { name: /time/i })
    expect(slider).toHaveAttribute('aria-valuetext', '14:00')
    expect(slider).not.toHaveAttribute('aria-valuetext', '840')
  })

  it('moving the slider updates instantUtc — sun, shadows and figures all derive from the one value (FR-022)', () => {
    render(<SolarTimeControlPanel />)
    const slider = screen.getByRole('slider', { name: /time/i })
    fireEvent.change(slider, { target: { value: String(9 * 60) } }) // 09:00 local

    const { moment } = useSolarAnalysisStore.getState()
    expect(moment!.localMinuteOfDay).toBe(9 * 60)
  })

  it('changing the date recomputes instantUtc for the new date (FR-020)', () => {
    render(<SolarTimeControlPanel />)
    const dateInput = screen.getByLabelText(/date/i)
    fireEvent.change(dateInput, { target: { value: '2026-12-21' } })

    const { moment } = useSolarAnalysisStore.getState()
    expect(moment!.localDate).toBe('2026-12-21')
  })

  it('play/stop toggles isPlaying, and stopping leaves the moment where it stopped (FR-021)', () => {
    render(<SolarTimeControlPanel />)
    const playButton = screen.getByRole('button', { name: /play/i })
    fireEvent.click(playButton)
    expect(useSolarAnalysisStore.getState().moment!.isPlaying).toBe(true)

    const instantBeforeStop = useSolarAnalysisStore.getState().moment!.instantUtc.getTime()
    const stopButton = screen.getByRole('button', { name: /stop/i })
    fireEvent.click(stopButton)

    expect(useSolarAnalysisStore.getState().moment!.isPlaying).toBe(false)
    expect(useSolarAnalysisStore.getState().moment!.instantUtc.getTime()).toBe(instantBeforeStop)
  })
})
