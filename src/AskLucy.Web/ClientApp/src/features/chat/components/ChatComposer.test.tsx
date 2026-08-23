import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe, toHaveNoViolations } from 'jest-axe'
import { afterEach, describe, expect, it, vi } from 'vitest'
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

describe('ChatComposer mic control — Push-to-Talk hold (US2, FR-005)', () => {
  afterEach(() => vi.useRealTimers())

  it('starts capture on pointer down and stops it on pointer up after a genuine hold', () => {
    vi.useFakeTimers()
    const { props } = renderComposer()
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.pointerDown(micButton)
    expect(props.onStartCapture).toHaveBeenCalledTimes(1)

    vi.advanceTimersByTime(500) // held well past the hold-vs-tap threshold
    fireEvent.pointerUp(micButton)
    expect(props.onStopCapture).toHaveBeenCalledTimes(1)
  })

  it('does not double-fire via the synthetic click that follows a pointer hold', () => {
    vi.useFakeTimers()
    const { props } = renderComposer()
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.pointerDown(micButton)
    vi.advanceTimersByTime(500)
    fireEvent.pointerUp(micButton)
    // Browsers fire a synthetic click after pointerup on the same element.
    fireEvent.click(micButton)

    expect(props.onStartCapture).toHaveBeenCalledTimes(1)
    expect(props.onStopCapture).toHaveBeenCalledTimes(1)
  })

  it('a quick tap (pointerdown/pointerup under the hold threshold) leaves capture running, not stopped', () => {
    const { props } = renderComposer()
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    // No time advanced between down and up — a real quick click, not a hold.
    fireEvent.pointerDown(micButton)
    fireEvent.pointerUp(micButton)
    fireEvent.click(micButton) // the synthetic click a browser fires after pointerup

    expect(props.onStartCapture).toHaveBeenCalledTimes(1)
    expect(props.onStopCapture).not.toHaveBeenCalled()
  })
})

describe('ChatComposer mic control — Push-to-Talk toggle (US2, Clarification Q1)', () => {
  it('starts capture on the first click and stops it on the second, with no preceding pointer events', () => {
    const { props, rerender } = renderComposer({ isListening: false })
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.click(micButton)
    expect(props.onStartCapture).toHaveBeenCalledTimes(1)

    rerender(
      <ChatComposer
        {...baseProps()}
        isListening={true}
        onStartCapture={props.onStartCapture}
        onStopCapture={props.onStopCapture}
        onClearCaptureError={props.onClearCaptureError}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: MIC_BUTTON_NAME }))
    expect(props.onStopCapture).toHaveBeenCalledTimes(1)
  })

  it('two quick taps (each a pointerdown/up/click sequence) toggle capture on then off', () => {
    const { props, rerender } = renderComposer({ isListening: false })
    let micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.pointerDown(micButton)
    fireEvent.pointerUp(micButton)
    fireEvent.click(micButton)
    expect(props.onStartCapture).toHaveBeenCalledTimes(1)
    expect(props.onStopCapture).not.toHaveBeenCalled()

    rerender(
      <ChatComposer
        {...baseProps()}
        isListening={true}
        onStartCapture={props.onStartCapture}
        onStopCapture={props.onStopCapture}
        onClearCaptureError={props.onClearCaptureError}
      />,
    )
    micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })

    fireEvent.pointerDown(micButton) // already listening — this "down" no-ops, doesn't restart capture
    fireEvent.pointerUp(micButton)
    fireEvent.click(micButton)
    expect(props.onStartCapture).toHaveBeenCalledTimes(1) // still just the first tap
    expect(props.onStopCapture).toHaveBeenCalledTimes(1)
  })
})

describe('ChatComposer mic control — keyboard hold (US2, FR-010, Space)', () => {
  afterEach(() => vi.useRealTimers())

  it('starts capture on Space keydown and stops it on Space keyup after a genuine hold, without hijacking the text field', () => {
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

  it('a quick Space press leaves capture running, not stopped', () => {
    const { props } = renderComposer()
    const micButton = screen.getByRole('button', { name: MIC_BUTTON_NAME })
    micButton.focus()

    fireEvent.keyDown(micButton, { key: ' ', code: 'Space' })
    fireEvent.keyUp(micButton, { key: ' ', code: 'Space' })

    expect(props.onStartCapture).toHaveBeenCalledTimes(1)
    expect(props.onStopCapture).not.toHaveBeenCalled()
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

// specs/029-fix-chat-widget-bugs research.md Decision 5: the plain inline "Cancel" button this
// suite previously tested (rendered off `isListening` alone) was replaced by the shared
// `RecordingReviewControls` component, driven by `recording.phase` — the same control
// `VoiceControlBar` (now retired) and `CollapsedVoiceControls` already used, so there's one
// recording-review implementation instead of two.
describe('ChatComposer — recording review (US3, specs/029-fix-chat-widget-bugs FR-005)', () => {
  it('shows RecordingReviewControls while a Push-to-Talk recording is in progress, and cancel discards without sending', () => {
    const onCancelRecording = vi.fn()
    renderComposer({
      isListening: true,
      recording: {
        phase: 'recording',
        getIntensity: () => 0,
        onFinish: vi.fn(),
        onCancelRecording,
      },
    })

    fireEvent.click(screen.getByRole('button', { name: /cancel recording/i }))
    expect(onCancelRecording).toHaveBeenCalledTimes(1)
    // The plain mic button is replaced, not merely covered, while reviewing.
    expect(screen.queryByRole('button', { name: MIC_BUTTON_NAME })).not.toBeInTheDocument()
  })

  it('does not show recording review controls while idle', () => {
    renderComposer({ isListening: false })
    expect(screen.queryByRole('button', { name: /cancel recording/i })).not.toBeInTheDocument()
  })

  // specs/031-voice-controls-redesign FR-001/FR-003, research.md Decision 1 — Finish now
  // transcribes directly; there is no longer a separate "send for transcription"/Accept
  // control between tapping Finish and the transcript landing in the message field.
  it('calls onFinish directly when Finish is tapped, with no intermediate accept control ever rendered', () => {
    const onFinish = vi.fn()
    renderComposer({
      isListening: true,
      recording: {
        phase: 'recording',
        getIntensity: () => 0,
        onFinish,
        onCancelRecording: vi.fn(),
      },
    })

    expect(screen.queryByRole('button', { name: /send recording for transcription/i })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /finished speaking/i }))
    expect(onFinish).toHaveBeenCalledTimes(1)
  })

  it('never shows a "send for transcription" control while transcribing', () => {
    renderComposer({
      isListening: true,
      recording: {
        phase: 'transcribing',
        getIntensity: () => 0,
        onFinish: vi.fn(),
        onCancelRecording: vi.fn(),
      },
    })
    expect(screen.queryByRole('button', { name: /send recording for transcription/i })).not.toBeInTheDocument()
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

describe('ChatComposer — mode-switch menu (US3, FR-006, Clarification Q3)', () => {
  it('opens a menu offering the other mode, and switching calls onToggleMode', async () => {
    const { props } = renderComposer({ conversationMode: 'Continuous' })
    fireEvent.click(screen.getByRole('button', { name: /voice input mode settings/i }))
    // getByText rather than getByRole('menuitem', ...) — MUI's Menu Popper/Grow transition
    // combined with jsdom's getComputedStyle throws on this environment's role-based
    // accessibility-tree walk (unrelated to this component's own correctness; the same
    // pattern is unprecedented elsewhere in this codebase's tests). Text content is a
    // faithful enough target for what this test actually verifies: clicking the menu's
    // offered action fires onToggleMode.
    const menuItem = await screen.findByText(/switch to push-to-talk/i)
    fireEvent.click(menuItem)
    expect(props.onToggleMode).toHaveBeenCalledTimes(1)
  })

  it('disables the mode-switch control while a Push-to-Talk capture is in progress', () => {
    renderComposer({ conversationMode: 'PushToTalk', isListening: true })
    expect(screen.getByRole('button', { name: /voice input mode settings/i })).toBeDisabled()
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
