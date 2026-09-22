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

  describe('US1 — typed time entry (specs/064)', () => {
    function timeEntryField() {
      return screen.getByRole('textbox', { name: /time/i }) as HTMLInputElement
    }

    it('a committed entry writes through setLocalMinuteOfDay, and typing alone does not move the view (FR-002, FR-020)', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      fireEvent.change(field, { target: { value: '06:41' } })
      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(14 * 60) // unchanged mid-type

      fireEvent.keyDown(field, { key: 'Enter' })
      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(6 * 60 + 41)
    })

    it('blur commits rather than discards (US1 scenario 2)', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      fireEvent.change(field, { target: { value: '09:15' } })
      fireEvent.blur(field)
      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(9 * 60 + 15)
    })

    it('committing the time already set causes no flicker or reset', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      fireEvent.change(field, { target: { value: '14:00' } })
      fireEvent.keyDown(field, { key: 'Enter' })
      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(14 * 60)
      expect(field).toHaveValue('14:00')
    })

    it('a rejected entry retains the typed text, retains the previous time, and shows a reason (FR-003, FR-005, FR-024)', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      fireEvent.change(field, { target: { value: '25:00' } })
      fireEvent.keyDown(field, { key: 'Enter' })

      expect(field).toHaveValue('25:00')
      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(14 * 60)
      expect(screen.getByRole('alert')).toBeInTheDocument()
    })

    it('committing stops playback (FR-009)', () => {
      render(<SolarTimeControlPanel />)
      fireEvent.click(screen.getByRole('button', { name: /play/i }))
      expect(useSolarAnalysisStore.getState().moment!.isPlaying).toBe(true)

      const field = timeEntryField()
      fireEvent.change(field, { target: { value: '10:00' } })
      fireEvent.keyDown(field, { key: 'Enter' })

      expect(useSolarAnalysisStore.getState().moment!.isPlaying).toBe(false)
    })

    it('disables the entry field while playing, and re-enables it once stopped', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      expect(field).not.toBeDisabled()

      fireEvent.click(screen.getByRole('button', { name: /play/i }))
      expect(field).toBeDisabled()

      fireEvent.click(screen.getByRole('button', { name: /stop/i }))
      expect(field).not.toBeDisabled()
    })

    it('selects all text on focus, so typing replaces rather than requiring a manual clear first', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      fireEvent.focus(field)
      expect(field.selectionStart).toBe(0)
      expect(field.selectionEnd).toBe(field.value.length)
    })

    it('ArrowUp/ArrowDown on the field step the committed time by one minute', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()

      fireEvent.keyDown(field, { key: 'ArrowUp' })
      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(14 * 60 + 1)

      fireEvent.keyDown(field, { key: 'ArrowDown' })
      fireEvent.keyDown(field, { key: 'ArrowDown' })
      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(14 * 60 - 1)
    })

    it('the spinner buttons step the committed time by one minute', () => {
      render(<SolarTimeControlPanel />)
      fireEvent.click(screen.getByRole('button', { name: /increase time/i }))
      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(14 * 60 + 1)

      fireEvent.click(screen.getByRole('button', { name: /decrease time/i }))
      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(14 * 60)
    })

    it('auto-inserts the colon as the user types digits', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      fireEvent.change(field, { target: { value: '0625' } })
      expect(field).toHaveValue('06:25')
    })

    it('changing the date clears an uncommitted draft (FR-010)', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      fireEvent.change(field, { target: { value: '08:30' } }) // typed, not committed

      const dateInput = screen.getByLabelText(/date/i)
      fireEvent.change(dateInput, { target: { value: '2026-12-21' } })

      // The stale draft must not apply — the field should show the moment's current time, not 08:30.
      expect(field).not.toHaveValue('08:30')
    })

    it('a spring-forward gap time is stated rather than silently resolved (FR-007)', async () => {
      // Egypt observes DST: this environment's tzdata places the 2026 spring-forward transition
      // at 2026-04-24, where the local clock jumps from 23:59 (April 23) straight to 01:00
      // (April 24) — 00:00-00:59 on the 24th does not exist.
      useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null })
      await useSolarAnalysisStore.getState().open(30.13, 31.75) // Cairo
      useSolarAnalysisStore.getState().setLocalDate('2026-04-24')

      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      fireEvent.change(field, { target: { value: '00:30' } })
      fireEvent.keyDown(field, { key: 'Enter' })

      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).not.toBe(30)
      expect(screen.getByRole('alert')).toBeInTheDocument()
    })

    it('an autumn fold time resolves consistently across repeated commits (FR-008)', async () => {
      // The 2026 fall-back transition puts local 23:00-23:59 (October 29) on the table twice; a
      // defined, deterministic rule (fromLocalParts's fixed-point convergence) must pick the same
      // one every time — the test pins consistency, not which of the two instants it picks.
      useSolarAnalysisStore.setState({ site: null, moment: null, status: 'idle', failureReason: null, buildingsNotice: null })
      await useSolarAnalysisStore.getState().open(30.13, 31.75) // Cairo
      useSolarAnalysisStore.getState().setLocalDate('2026-10-29')

      render(<SolarTimeControlPanel />)
      const field = timeEntryField()

      fireEvent.change(field, { target: { value: '23:15' } })
      fireEvent.keyDown(field, { key: 'Enter' })
      const first = useSolarAnalysisStore.getState().moment!.instantUtc.getTime()

      fireEvent.change(field, { target: { value: '20:00' } }) // move away
      fireEvent.keyDown(field, { key: 'Enter' })
      fireEvent.change(field, { target: { value: '23:15' } }) // commit the fold time again
      fireEvent.keyDown(field, { key: 'Enter' })
      const second = useSolarAnalysisStore.getState().moment!.instantUtc.getTime()

      expect(second).toBe(first)
    })

    it('the field follows the store: a slider move updates it, and a commit does not stick after a later drag (US1 scenario 6)', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      const slider = screen.getByRole('slider', { name: /time/i })

      fireEvent.change(slider, { target: { value: String(11 * 60) } })
      expect(field).toHaveValue('11:00')

      fireEvent.change(field, { target: { value: '06:41' } })
      fireEvent.keyDown(field, { key: 'Enter' })
      expect(field).toHaveValue('06:41')

      fireEvent.change(slider, { target: { value: String(16 * 60) } })
      expect(field).toHaveValue('16:00') // takes over cleanly, no reversion to 06:41
    })

    it('an entered time is interpreted in the site timezone, not the browser default (FR-006)', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      fireEvent.change(field, { target: { value: '09:00' } })
      fireEvent.keyDown(field, { key: 'Enter' })

      // Asia/Dubai is UTC+4 with no DST: 09:00 local -> 05:00Z.
      const instantUtc = useSolarAnalysisStore.getState().moment!.instantUtc
      expect(instantUtc.getUTCHours()).toBe(5)
      expect(instantUtc.getUTCMinutes()).toBe(0)
    })

    it('aria-valuetext still carries the local time after a typed commit (FR-021)', () => {
      render(<SolarTimeControlPanel />)
      const field = timeEntryField()
      fireEvent.change(field, { target: { value: '07:05' } })
      fireEvent.keyDown(field, { key: 'Enter' })

      const slider = screen.getByRole('slider', { name: /time/i })
      expect(slider).toHaveAttribute('aria-valuetext', '07:05')
    })
  })

  describe('US2 — tick marks (specs/064)', () => {
    it('renders marks on the slider without disturbing aria-valuetext (FR-013, FR-022)', () => {
      render(<SolarTimeControlPanel />)
      const slider = screen.getByRole('slider', { name: /time/i })
      expect(slider).toHaveAttribute('aria-valuetext', '14:00')
      // jsdom never measures (research D5) -> Full tier -> quarter-hour marks present.
      const marks = document.querySelectorAll('.MuiSlider-mark')
      expect(marks.length).toBeGreaterThan(0)
    })
  })

  describe('US3 — snap on drag, one minute on keys (specs/064)', () => {
    it('an arrow-key press moves exactly one minute and is not snapped (FR-017, SC-006)', () => {
      render(<SolarTimeControlPanel />)
      const slider = screen.getByRole('slider', { name: /time/i })
      slider.focus()
      fireEvent.keyDown(slider, { key: 'ArrowRight' })
      fireEvent.change(slider, { target: { value: String(14 * 60 + 1) } })
      fireEvent.keyUp(slider, { key: 'ArrowRight' })

      expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(14 * 60 + 1)
    })

    it('repeated arrow-key presses each move one minute, none snapped (US3 scenario 3, SC-006)', () => {
      render(<SolarTimeControlPanel />)
      const slider = screen.getByRole('slider', { name: /time/i })
      slider.focus()

      for (let i = 1; i <= 3; i++) {
        fireEvent.keyDown(slider, { key: 'ArrowRight' })
        fireEvent.change(slider, { target: { value: String(14 * 60 + i) } })
        fireEvent.keyUp(slider, { key: 'ArrowRight' })
        expect(useSolarAnalysisStore.getState().moment!.localMinuteOfDay).toBe(14 * 60 + i)
      }
    })

    it('a pointer drag settles on a 15-minute boundary via onChangeCommitted', () => {
      render(<SolarTimeControlPanel />)
      const slider = screen.getByRole('slider', { name: /time/i })
      // Dispatched on the wrapping Box (the slider's own ancestor), not the thumb: this still
      // reaches our onPointerDownCapture handler on its way down, but never reaches MUI Slider's
      // own internal pointerdown listener on the thumb — which computes position from
      // getBoundingClientRect() and crashes on jsdom's all-zero layout metrics (research D6).
      const sliderTrack = slider.closest('.MuiSlider-root')!.parentElement!
      fireEvent.pointerDown(sliderTrack)
      fireEvent.change(slider, { target: { value: String(9 * 60 + 7) } }) // 09:07 — off-boundary, commits via the hidden input's change handler

      const localMinuteOfDay = useSolarAnalysisStore.getState().moment!.localMinuteOfDay
      expect(localMinuteOfDay % 15).toBe(0)
    })

    // A genuine mid-drag "onChange fired, onChangeCommitted not yet" moment isn't observable via
    // fireEvent in jsdom: MUI's hidden <input> change handler (useSlider.mjs's changeValue) fires
    // onChange and onChangeCommitted together for any DOM 'change' event, and a real pointer drag
    // bypasses that input entirely via document-level mouse listeners (research D6 — pointer
    // dragging is close to untestable here). The policy this would have covered — onChange itself
    // never snaps — is already proven by the keyboard test above (same handler, source: 'keyboard',
    // result unsnapped) and the pointer test below (source: 'pointer', result snapped).

    it('slider movement stops at the boundaries rather than wrapping (FR-018)', () => {
      render(<SolarTimeControlPanel />)
      const slider = screen.getByRole('slider', { name: /time/i })
      expect(slider).toHaveAttribute('aria-valuemax', '1439')
      expect(slider).toHaveAttribute('aria-valuemin', '0')
    })
  })
})
