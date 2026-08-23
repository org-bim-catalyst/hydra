import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import { transcribeAudio } from '../api/aiApi'
import { usePdfTextExtraction } from '../pdf/usePdfTextExtraction'
import { ChatComposer, type ChatComposerProps } from './ChatComposer'

vi.mock('../api/aiApi', () => ({ transcribeAudio: vi.fn() }))
vi.mock('../pdf/usePdfTextExtraction', () => ({ usePdfTextExtraction: vi.fn() }))

// Sensible default so every other test in this file (which doesn't care about attach-file
// dispatch) can render the composer without configuring this mock itself.
vi.mocked(usePdfTextExtraction).mockReturnValue({ extractText: vi.fn().mockResolvedValue('') })

expect.extend(toHaveNoViolations)

const MIC_BUTTON_NAME = /^(start|stop) voice input$/i

function baseProps(): ChatComposerProps {
  return {
    value: '',
    onChange: vi.fn(),
    onSend: vi.fn(),
    disabled: false,
    conversationMode: 'PushToTalk',
    isListening: false,
    permissionState: 'unknown',
    captureError: null,
    onStartCapture: vi.fn(),
    onStopCapture: vi.fn(),
    onClearCaptureError: vi.fn(),
    onToggleMode: vi.fn(),
  }
}

function renderComposer(overrides: Partial<ChatComposerProps> = {}) {
  const props: ChatComposerProps = { ...baseProps(), ...overrides }
  return { ...render(<ChatComposer {...props} />), props }
}

// specs/034-transcription-crash-gesture-and-continuous-view FR-004/FR-005/FR-006/FR-007 —
// restores the dual gesture specs/033 had removed: a tap starts recording and shows explicit
// confirm/discard controls (waiting for the user); a hold shows only the waveform and finishes
// automatically on release. A tap and a hold are physically identical at press time — they only
// resolve into one or the other at release, based on elapsed hold duration.
describe('ChatComposer mic control — hold-to-talk (US2, FR-006)', () => {
  afterEach(() => vi.useRealTimers())

  it('starts capture on pointer down and, once held past the threshold, stops it on release with no review controls ever shown', () => {
    vi.useFakeTimers()
    const { props } = renderComposer()
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.pointerDown(micButton)
    expect(props.onStartCapture).toHaveBeenCalledTimes(1)

    vi.advanceTimersByTime(500) // held well past the hold threshold
    fireEvent.pointerUp(micButton)

    expect(props.onStopCapture).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('button', { name: /finished speaking/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /cancel recording/i })).not.toBeInTheDocument()
  })

  it('calls setPointerCapture on pointerdown with the event pointerId (the specs/033 release-lost-to-a-DOM-swap bug fix, unaffected by this gesture restoration)', () => {
    const setPointerCapture = vi.fn()
    // jsdom does not implement Pointer Capture; stub it on the prototype so the component's
    // optional-chained call has something to invoke and assert against.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    ;(HTMLElement.prototype as any).setPointerCapture = setPointerCapture
    renderComposer()
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.pointerDown(micButton, { pointerId: 7 })

    expect(setPointerCapture).toHaveBeenCalledWith(7)
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    delete (HTMLElement.prototype as any).setPointerCapture
  })

  it('pointerleave after the hold threshold also stops-and-transcribes directly', () => {
    vi.useFakeTimers()
    const { props } = renderComposer()
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.pointerDown(micButton)
    vi.advanceTimersByTime(500)
    fireEvent.pointerLeave(micButton)

    expect(props.onStopCapture).toHaveBeenCalledTimes(1)
  })

  it('pointercancel after the hold threshold also stops-and-transcribes directly', () => {
    vi.useFakeTimers()
    const { props } = renderComposer()
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.pointerDown(micButton)
    vi.advanceTimersByTime(500)
    fireEvent.pointerCancel(micButton)

    expect(props.onStopCapture).toHaveBeenCalledTimes(1)
  })

  it('a pointerup with no preceding capture on this control (isListening false) does not call onStopCapture', () => {
    const { props } = renderComposer({ isListening: false })
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.pointerUp(micButton)

    expect(props.onStopCapture).not.toHaveBeenCalled()
  })
})

// In real usage `recording.phase` only becomes 'recording' once the parent's recorder state
// catches up asynchronously *after* `onStartCapture()` fires — there's always a real gap between
// pointerdown and that prop update landing. These tests model that with `rerender`, the same
// pattern this file already uses elsewhere for async recording-state transitions: passing
// `recording.phase: 'recording'` from the very first render would make the defensive
// `isCapturing()` fallback (research.md Decision 3's remount guard) treat pointerdown as
// already-in-progress and no-op it entirely, which doesn't happen in real usage.
describe('ChatComposer mic control — tap-to-record with review (US2, FR-004/FR-005)', () => {
  function startThenResolveAsRecording(
    onFinish = vi.fn(),
    onCancelRecording = vi.fn(),
  ) {
    const { props, rerender } = renderComposer({ isListening: false })
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.pointerDown(micButton)
    expect(props.onStartCapture).toHaveBeenCalledTimes(1)

    rerender(
      <ChatComposer
        {...baseProps()}
        isListening
        onStartCapture={props.onStartCapture}
        onStopCapture={props.onStopCapture}
        onClearCaptureError={props.onClearCaptureError}
        recording={{ phase: 'recording', getIntensity: () => 0, onFinish, onCancelRecording }}
      />,
    )

    return { props, micButton: screen.getByRole('button', { name: MIC_BUTTON_NAME }) }
  }

  it('a quick tap (pointerdown immediately followed by pointerup) leaves recording active and shows confirm/discard controls plus the waveform, without calling onStopCapture', () => {
    const { props, micButton } = startThenResolveAsRecording()

    fireEvent.pointerUp(micButton)

    expect(props.onStopCapture).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: /finished speaking/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /cancel recording/i })).toBeInTheDocument()
  })

  it('tapping Finish on a tap-resolved recording calls onStopCapture and hides the review controls', () => {
    const { props, micButton } = startThenResolveAsRecording()
    fireEvent.pointerUp(micButton)

    fireEvent.click(screen.getByRole('button', { name: /finished speaking/i }))

    expect(props.onStopCapture).toHaveBeenCalledTimes(1)
  })

  it('tapping Cancel on a tap-resolved recording calls recording.onCancelRecording and does not call onStopCapture', () => {
    const onCancelRecording = vi.fn()
    const { props, micButton } = startThenResolveAsRecording(vi.fn(), onCancelRecording)
    fireEvent.pointerUp(micButton)

    fireEvent.click(screen.getByRole('button', { name: /cancel recording/i }))

    expect(onCancelRecording).toHaveBeenCalledTimes(1)
    expect(props.onStopCapture).not.toHaveBeenCalled()
  })
})

describe('ChatComposer mic control — keyboard hold and tap (US2, FR-010, Space)', () => {
  afterEach(() => vi.useRealTimers())

  it('starts capture on Space keydown and, once held past the threshold, stops it on Space keyup with no review controls shown, without hijacking the text field', () => {
    vi.useFakeTimers()
    const { props } = renderComposer()
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })
    micButton.focus()

    fireEvent.keyDown(micButton, { key: ' ', code: 'Space' })
    expect(props.onStartCapture).toHaveBeenCalledTimes(1)

    vi.advanceTimersByTime(500)
    fireEvent.keyUp(micButton, { key: ' ', code: 'Space' })
    expect(props.onStopCapture).toHaveBeenCalledTimes(1)
  })

  it('a brief Space press resolves as a tap — shows review controls, does not call onStopCapture', () => {
    const { props, rerender } = renderComposer({ isListening: false })
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })
    micButton.focus()

    fireEvent.keyDown(micButton, { key: ' ', code: 'Space' })
    expect(props.onStartCapture).toHaveBeenCalledTimes(1)

    // Same async-catch-up modeling as the pointer tap tests above.
    rerender(
      <ChatComposer
        {...baseProps()}
        isListening
        onStartCapture={props.onStartCapture}
        onStopCapture={props.onStopCapture}
        onClearCaptureError={props.onClearCaptureError}
        recording={{ phase: 'recording', getIntensity: () => 0, onFinish: vi.fn(), onCancelRecording: vi.fn() }}
      />,
    )
    fireEvent.keyUp(screen.getByRole('button', { name: MIC_BUTTON_NAME }), { key: ' ', code: 'Space' })

    expect(props.onStopCapture).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: /finished speaking/i })).toBeInTheDocument()
  })

  it('does not repeat-fire onStartCapture while a key is held down (key-repeat keydown events)', () => {
    const { props } = renderComposer()
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })
    micButton.focus()

    fireEvent.keyDown(micButton, { key: ' ', code: 'Space' })
    fireEvent.keyDown(micButton, { key: ' ', code: 'Space', repeat: true })
    fireEvent.keyDown(micButton, { key: ' ', code: 'Space', repeat: true })

    expect(props.onStartCapture).toHaveBeenCalledTimes(1)
  })

  it('does not start capture when Space is pressed while the text field has focus', () => {
    const { props } = renderComposer()
    const textField = screen.getByPlaceholderText('Message Ask Lucy...')
    textField.focus()

    fireEvent.keyDown(textField, { key: ' ', code: 'Space' })

    expect(props.onStartCapture).not.toHaveBeenCalled()
  })
})

// specs/033-hold-to-talk-and-echo-fix — with pure hold-to-talk, releasing the mic button is the
// only way a recording finishes, so the previous Finish/Cancel `RecordingReviewControls` swap is
// removed from this component entirely. The mic button stays mounted (and is the fix for the
// pointer-capture bug); only its visual state (disabled, color) changes during 'transcribing'.
describe('ChatComposer — active recording (US2, specs/033-hold-to-talk-and-echo-fix FR-007/FR-008)', () => {
  it('keeps the mic button mounted (not replaced) and shows no Finish/Cancel controls while a Push-to-Talk recording is in progress', () => {
    renderComposer({
      isListening: true,
      recording: {
        phase: 'recording',
        getIntensity: () => 0,
        onFinish: vi.fn(),
        onCancelRecording: vi.fn(),
      },
    })

    expect(screen.getByRole('button', { name: MIC_BUTTON_NAME })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /cancel recording/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /finished speaking/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /send recording for transcription/i })).not.toBeInTheDocument()
  })

  it('does not show any recording-review affordance while idle', () => {
    renderComposer({ isListening: false })
    expect(screen.queryByRole('button', { name: /cancel recording/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /finished speaking/i })).not.toBeInTheDocument()
  })

  it('disables the mic button while transcribing, and shows no Finish/Cancel/Accept controls', () => {
    renderComposer({
      isListening: true,
      recording: {
        phase: 'transcribing',
        getIntensity: () => 0,
        onFinish: vi.fn(),
        onCancelRecording: vi.fn(),
      },
    })

    expect(screen.getByRole('button', { name: MIC_BUTTON_NAME })).toBeDisabled()
    expect(screen.queryByRole('button', { name: /cancel recording/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /send recording for transcription/i })).not.toBeInTheDocument()
  })

  it('releasing the mic button while an active recording is the only trigger — onStopCapture fires, not a separate Finish click', () => {
    const { props } = renderComposer({
      isListening: true,
      recording: {
        phase: 'recording',
        getIntensity: () => 0,
        onFinish: vi.fn(),
        onCancelRecording: vi.fn(),
      },
    })

    fireEvent.pointerUp(screen.getByRole('button', { name: MIC_BUTTON_NAME }))
    expect(props.onStopCapture).toHaveBeenCalledTimes(1)
  })
})

describe('ChatComposer — Continuous mode (specs/029-fix-chat-widget-bugs FR-004/FR-006/FR-014)', () => {
  it('renders the same single mic button in Continuous mode as in Push-to-Talk — no separate control, no gate hiding it', () => {
    renderComposer({ conversationMode: 'Continuous' })
    expect(screen.getByRole('button', { name: MIC_BUTTON_NAME })).toBeInTheDocument()
  })

  it('toggles listening via a plain click in Continuous mode (no hold gesture)', () => {
    const { props } = renderComposer({ conversationMode: 'Continuous', isListening: false })
    fireEvent.click(screen.getByRole('button', { name: MIC_BUTTON_NAME }))
    expect(props.onStartCapture).toHaveBeenCalledTimes(1)
  })

  it('does not show a "Listening…" text label while capturing, in either mode (FR-014)', () => {
    renderComposer({ conversationMode: 'Continuous', isListening: true })
    expect(screen.queryByText(/listening…/i)).not.toBeInTheDocument()
  })

  // specs/031-voice-controls-redesign FR-004/US4 — Continuous mode never has a `recording`
  // sub-state, so it's structurally impossible for `RecordingReviewControls` (waveform,
  // Finish, Cancel) to appear there, unaffected by US1-US3's changes to the PTT recording
  // flow.
  it('never shows RecordingReviewControls, even while listening (Continuous has no recording sub-state)', () => {
    renderComposer({ conversationMode: 'Continuous', isListening: true })
    expect(screen.queryByRole('button', { name: /finished speaking/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /cancel recording/i })).not.toBeInTheDocument()
  })

  // specs/031-voice-controls-redesign FR-006 — each mode's idle view shows no control
  // exclusive to the other mode.
  it('idle view shows no Push-to-Talk-only affordance (no recording/mode-switch-blocked hint)', () => {
    renderComposer({ conversationMode: 'Continuous', isListening: false })
    expect(screen.queryByRole('button', { name: /finished speaking/i })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /voice input mode settings/i })).not.toBeDisabled()
  })
})

describe('ChatComposer — exactly one mic control (US3, FR-004)', () => {
  it('renders exactly one mic button, not two, in Push-to-Talk', () => {
    renderComposer({ conversationMode: 'PushToTalk' })
    expect(screen.getAllByRole('button', { name: MIC_BUTTON_NAME })).toHaveLength(1)
  })

  it('renders exactly one mic button, not two, in Continuous mode', () => {
    renderComposer({ conversationMode: 'Continuous' })
    expect(screen.getAllByRole('button', { name: MIC_BUTTON_NAME })).toHaveLength(1)
  })
})

// specs/032-transcription-and-mode-switch-fixes US2/FR-006 — the prior two-click dropdown
// (open a menu, then click its one option) is removed; a single click on the mode-switch
// icon now toggles the mode directly.
describe('ChatComposer — mode-switch toggle (US2, FR-006/FR-007/FR-008)', () => {
  it('a single click on the mode-switch icon calls onToggleMode directly, with no menu', () => {
    const { props } = renderComposer({ conversationMode: 'Continuous' })
    fireEvent.click(screen.getByRole('button', { name: /voice input mode settings/i }))

    expect(props.onToggleMode).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('a single click from Push-to-Talk also calls onToggleMode directly, with no menu', () => {
    const { props } = renderComposer({ conversationMode: 'PushToTalk' })
    fireEvent.click(screen.getByRole('button', { name: /voice input mode settings/i }))

    expect(props.onToggleMode).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('disables the mode-switch control while a Push-to-Talk capture is in progress (FR-007, unchanged)', () => {
    const { props } = renderComposer({ conversationMode: 'PushToTalk', isListening: true })
    const button = screen.getByRole('button', { name: /voice input mode settings/i })

    expect(button).toBeDisabled()
    fireEvent.click(button)
    expect(props.onToggleMode).not.toHaveBeenCalled()
  })

  it('the tooltip describes the target mode directly, in both directions (FR-008)', async () => {
    const { rerender } = renderComposer({ conversationMode: 'Continuous' })
    await userEvent.hover(screen.getByRole('button', { name: /voice input mode settings/i }))
    expect(await screen.findByText(/switch to push-to-talk/i)).toBeInTheDocument()

    rerender(<ChatComposer {...baseProps()} conversationMode="PushToTalk" />)
    await userEvent.hover(screen.getByRole('button', { name: /voice input mode settings/i }))
    expect(await screen.findByText(/switch to continuous conversation/i)).toBeInTheDocument()
  })
})

// specs/031-voice-controls-redesign FR-011, US6 — the merged speaker-mute/stop control
// (specs/029-fix-chat-widget-bugs FR-006a/FR-006b) has moved to ExpandedChatPanel's
// header; see ExpandedChatPanel.test.tsx for its coverage now.
describe('ChatComposer — no speaker-mute control in the footer (specs/031-voice-controls-redesign FR-011)', () => {
  it('renders no mute/unmute control in the composer, and no separate stop button either', () => {
    renderComposer()
    expect(screen.queryByRole('button', { name: /mute lucy/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /unmute lucy/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /stop/i })).not.toBeInTheDocument()
  })

  it('does not show a "Lucy is speaking…" text label anywhere (FR-013)', () => {
    renderComposer()
    expect(screen.queryByText(/lucy is speaking/i)).not.toBeInTheDocument()
  })
})

// specs/031-voice-controls-redesign FR-010, US5 — the translate control from
// specs/029-fix-chat-widget-bugs is removed entirely, not merely hidden or relocated.
describe('ChatComposer — translate control removed (specs/031-voice-controls-redesign FR-010)', () => {
  it('renders no translate control in any composer state', () => {
    const { rerender } = renderComposer()
    expect(screen.queryByRole('button', { name: /translate/i })).not.toBeInTheDocument()

    rerender(<ChatComposer {...baseProps()} isListening captureError="Something failed" />)
    expect(screen.queryByRole('button', { name: /translate/i })).not.toBeInTheDocument()
  })
})

describe('ChatComposer — controlled text field (US2 refactor)', () => {
  it('calls onChange as the user types, and onSend when Enter is pressed', () => {
    const { props } = renderComposer({ value: 'Hello' })
    const textField = screen.getByPlaceholderText('Message Ask Lucy...')
    expect(textField).toHaveValue('Hello')

    fireEvent.change(textField, { target: { value: 'Hello there' } })
    expect(props.onChange).toHaveBeenCalledWith('Hello there')

    fireEvent.keyDown(textField, { key: 'Enter' })
    expect(props.onSend).toHaveBeenCalledTimes(1)
  })
})

describe('ChatComposer accessibility (SPEC-013 T020, constitution §7/§10)', () => {
  it('has no automatically detectable a11y violations in Push-to-Talk, idle', async () => {
    const { container } = renderComposer()
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in Push-to-Talk, listening', async () => {
    const { container } = renderComposer({ isListening: true })
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in Continuous mode', async () => {
    const { container } = renderComposer({ conversationMode: 'Continuous', isListening: true })
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with a permission-denied warning shown', async () => {
    const { container } = renderComposer({ permissionState: 'denied' })
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations while transcribing a Push-to-Talk recording', async () => {
    const { container } = renderComposer({
      isListening: true,
      recording: { phase: 'transcribing', getIntensity: () => 0, onFinish: vi.fn(), onCancelRecording: vi.fn() },
    })
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with the voice-preferences-unavailable indicator shown', async () => {
    const { container } = renderComposer({ voicePreferencesUnavailable: true })
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('ChatComposer — capped growth (specs/030-composer-panel-refinements FR-003/FR-004/FR-006)', () => {
  it('renders a multiline textarea with a non-empty, fixed line-height driving the 6-row cap', () => {
    renderComposer()
    const textField = screen.getByPlaceholderText('Message Ask Lucy...')
    expect(textField.tagName.toLowerCase()).toBe('textarea')
    // A concrete px value (not '', 'normal', or 'inherit') confirms the explicit lineHeight
    // sx rule (research.md Decision 2) is actually being applied, not left to an ambient
    ///inherited value that could vary by container.
    expect(getComputedStyle(textField).lineHeight).toMatch(/^\d+(\.\d+)?px$/)
  })

  it('reflects shorter content after a longer value is replaced, with the same fixed line-height cap (FR-006)', () => {
    const longValue = Array.from({ length: 10 }, (_, i) => `line ${i}`).join('\n')
    const { rerender } = renderComposer({ value: longValue })
    const grownField = screen.getByPlaceholderText('Message Ask Lucy...')
    expect(grownField).toHaveValue(longValue)
    const grownLineHeight = getComputedStyle(grownField).lineHeight

    const shortValue = 'line 0\nline 1'
    rerender(<ChatComposer {...baseProps()} value={shortValue} />)
    const shrunkField = screen.getByPlaceholderText('Message Ask Lucy...')
    expect(shrunkField).toHaveValue(shortValue)
    // The same fixed line-height cap governs the field regardless of content length — it
    // isn't a value that grew with the longer content and got left behind after shrinking.
    expect(getComputedStyle(shrunkField).lineHeight).toBe(grownLineHeight)
  })
})

describe('ChatComposer — two-row layout (specs/030-composer-panel-refinements FR-001/FR-002/FR-005)', () => {
  it('groups every footer control under one row, separate from the text field', () => {
    renderComposer()
    const textField = screen.getByPlaceholderText('Message Ask Lucy...')
    const textFieldRoot = textField.closest('.MuiFormControl-root')
    const sendButton = screen.getByRole('button', { name: /send message/i })
    const attachButton = screen.getByRole('button', { name: /attach file/i })

    expect(textFieldRoot).not.toBeNull()
    // The send button is wrapped in a <span> (Tooltip's disabled-element workaround), so
    // compare against the nearest shared Stack ancestor rather than the immediate parent.
    const footerRow = sendButton.closest('.MuiStack-root')
    expect(footerRow).not.toBeNull()
    // The footer row is no longer the text field's immediate sibling container.
    expect(textFieldRoot?.parentElement).not.toBe(footerRow)
    // Every footer control shares one common ancestor row (the fixed footer row).
    expect(attachButton.closest('.MuiStack-root')).toBe(footerRow)
  })
})

describe('ChatComposer — icon-button tooltips (specs/030-composer-panel-refinements FR-010/FR-012)', () => {
  it('shows a tooltip for Attach file on hover', async () => {
    const user = userEvent.setup()
    renderComposer()
    await user.hover(screen.getByRole('button', { name: /attach file/i }))
    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveTextContent('Attach file'))
  })

  it('shows a tooltip for Insert saved prompt on hover', async () => {
    const user = userEvent.setup()
    renderComposer({ onInsertPromptClick: vi.fn() })
    await user.hover(screen.getByRole('button', { name: /insert saved prompt/i }))
    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveTextContent('Insert saved prompt'))
  })

  it('shows a contextual tooltip for the mic button that matches its current aria-label', async () => {
    const user = userEvent.setup()
    const { rerender } = renderComposer({ isListening: false })
    await user.hover(screen.getByRole('button', { name: MIC_BUTTON_NAME }))
    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveTextContent('Start voice input'))
    await user.unhover(screen.getByRole('button', { name: MIC_BUTTON_NAME }))

    rerender(<ChatComposer {...baseProps()} isListening={true} />)
    await user.hover(screen.getByRole('button', { name: MIC_BUTTON_NAME }))
    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveTextContent('Stop voice input'))
  })

  it('shows a tooltip for Send message, even while the button is disabled', async () => {
    const user = userEvent.setup()
    renderComposer({ value: '' })
    const sendButton = screen.getByRole('button', { name: /send message/i })
    expect(sendButton).toBeDisabled()
    // The disabled button itself has pointer-events: none, so hover the Tooltip's <span>
    // wrapper instead — the same element real pointer events would land on.
    await user.hover(sendButton.parentElement as HTMLElement)
    await waitFor(() => expect(screen.getByRole('tooltip')).toHaveTextContent('Send message'))
  })
})

describe('ChatComposer — recording-state declutter (specs/031-voice-controls-redesign FR-006/FR-008)', () => {
  it('hides attach, insert-prompt, and the mode-switch control while a recording is active', () => {
    renderComposer({
      isListening: true,
      onInsertPromptClick: vi.fn(),
      recording: { phase: 'recording', getIntensity: () => 0, onFinish: vi.fn(), onCancelRecording: vi.fn() },
    })

    expect(screen.queryByRole('button', { name: /attach file/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /insert saved prompt/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /voice input mode settings/i })).not.toBeInTheDocument()
  })

  it('hides the voice-preferences-unavailable indicator while a recording is active', () => {
    renderComposer({
      isListening: true,
      voicePreferencesUnavailable: true,
      recording: { phase: 'recording', getIntensity: () => 0, onFinish: vi.fn(), onCancelRecording: vi.fn() },
    })
    expect(
      screen.queryByLabelText(/voice preferences unavailable, using defaults/i),
    ).not.toBeInTheDocument()
  })

  it('restores attach, insert-prompt, and mode-switch once back to idle', () => {
    const { rerender } = renderComposer({
      isListening: true,
      onInsertPromptClick: vi.fn(),
      recording: { phase: 'recording', getIntensity: () => 0, onFinish: vi.fn(), onCancelRecording: vi.fn() },
    })
    expect(screen.queryByRole('button', { name: /attach file/i })).not.toBeInTheDocument()

    rerender(<ChatComposer {...baseProps()} onInsertPromptClick={vi.fn()} />)

    expect(screen.getByRole('button', { name: /attach file/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /insert saved prompt/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /voice input mode settings/i })).toBeInTheDocument()
  })

  it('keeps attach/insert-prompt/mode-switch visible in Continuous mode (recording is never active there)', () => {
    renderComposer({ conversationMode: 'Continuous', onInsertPromptClick: vi.fn() })
    expect(screen.getByRole('button', { name: /attach file/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /insert saved prompt/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /voice input mode settings/i })).toBeInTheDocument()
  })
})

describe('ChatComposer — microphone permission (US2, FR-009)', () => {
  it('surfaces a visible, specific error when capture fails, instead of failing silently', () => {
    renderComposer({ captureError: 'Microphone access was denied.' })
    expect(screen.getByText('Microphone access was denied.')).toBeInTheDocument()
  })

  it('shows a visible, actionable warning when microphone permission was denied', () => {
    renderComposer({ permissionState: 'denied' })
    expect(screen.getByText(/microphone access was denied/i)).toBeInTheDocument()
  })

  it('shows no permission warning while permission state is unknown or granted', () => {
    const { rerender } = renderComposer({ permissionState: 'unknown' })
    expect(screen.queryByText(/microphone access was denied/i)).not.toBeInTheDocument()

    rerender(<ChatComposer {...baseProps()} permissionState="granted" />)
    expect(screen.queryByText(/microphone access was denied/i)).not.toBeInTheDocument()
  })
})

// specs/031-voice-controls-redesign FR-013, T032 (added via /speckit-analyze finding E1) —
// `ChatComposer.tsx` is edited three separate times in this feature (US3/US5/US6); this
// regression test confirms the attach-file dispatch it was never meant to touch still
// works for all three supported formats afterward.
describe('ChatComposer — attach-file format dispatch (specs/031-voice-controls-redesign FR-013)', () => {
  function getFileInput(container: HTMLElement) {
    return container.querySelector('input[type="file"]') as HTMLInputElement
  }

  it('dispatches a PDF through usePdfTextExtraction and appends the extracted text', async () => {
    const extractText = vi.fn().mockResolvedValue('extracted pdf text')
    vi.mocked(usePdfTextExtraction).mockReturnValueOnce({ extractText })
    const { props, container } = renderComposer({ value: 'existing ' })

    const file = new File(['%PDF-1.4'], 'doc.pdf', { type: 'application/pdf' })
    fireEvent.change(getFileInput(container), { target: { files: [file] } })

    await waitFor(() => expect(extractText).toHaveBeenCalledWith(file))
    await waitFor(() => expect(props.onChange).toHaveBeenCalledWith('existing extracted pdf text'))
  })

  it('dispatches an audio file through transcribeAudio and appends the transcript', async () => {
    vi.mocked(transcribeAudio).mockResolvedValueOnce('transcribed audio text')
    const { props, container } = renderComposer({ value: 'existing ' })

    const file = new File(['fake-audio'], 'clip.webm', { type: 'audio/webm' })
    fireEvent.change(getFileInput(container), { target: { files: [file] } })

    await waitFor(() => expect(transcribeAudio).toHaveBeenCalledWith(file))
    await waitFor(() =>
      expect(props.onChange).toHaveBeenCalledWith('existing transcribed audio text'),
    )
  })

  it('dispatches a CSV file by appending its raw text', async () => {
    const { props, container } = renderComposer({ value: 'existing ' })

    const file = new File(['a,b,c\n1,2,3'], 'data.csv', { type: 'text/csv' })
    fireEvent.change(getFileInput(container), { target: { files: [file] } })

    await waitFor(() => expect(props.onChange).toHaveBeenCalledWith('existing a,b,c\n1,2,3'))
  })

  it('accepts .pdf, .csv, and audio/* via the file input\'s accept attribute', () => {
    const { container } = renderComposer()
    expect(getFileInput(container)).toHaveAttribute('accept', '.pdf,.csv,audio/*')
  })
})
