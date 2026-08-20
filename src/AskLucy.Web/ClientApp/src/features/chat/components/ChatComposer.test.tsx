import { fireEvent, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ChatComposer, type ChatComposerProps } from './ChatComposer'

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
    isMuted: false,
    onToggleMute: vi.fn(),
    onTranslateLastClick: vi.fn(),
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
        onAccept: vi.fn(),
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

describe('ChatComposer — merged speaker-mute/stop control (US3, FR-006a/FR-006b)', () => {
  it('renders exactly one speaker-mute control, no separate stop button', () => {
    renderComposer()
    expect(screen.getByRole('button', { name: /mute lucy/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /stop/i })).not.toBeInTheDocument()
  })

  it('calls onToggleMute when the mute control is pressed, reflecting current isMuted state', () => {
    const { props } = renderComposer({ isMuted: false })
    fireEvent.click(screen.getByRole('button', { name: /mute lucy/i }))
    expect(props.onToggleMute).toHaveBeenCalledTimes(1)
  })

  it('shows "Unmute" affordance once muted', () => {
    renderComposer({ isMuted: true })
    expect(screen.getByRole('button', { name: /unmute lucy/i })).toBeInTheDocument()
  })

  it('does not show a "Lucy is speaking…" text label anywhere (FR-013)', () => {
    renderComposer()
    expect(screen.queryByText(/lucy is speaking/i)).not.toBeInTheDocument()
  })
})

describe('ChatComposer — relocated translate control (US4, FR-007)', () => {
  it('renders a translate control that calls onTranslateLastClick', () => {
    const { props } = renderComposer()
    fireEvent.click(screen.getByRole('button', { name: /translate last response/i }))
    expect(props.onTranslateLastClick).toHaveBeenCalledTimes(1)
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

  it('has no automatically detectable a11y violations while reviewing a Push-to-Talk recording', async () => {
    const { container } = renderComposer({
      isListening: true,
      recording: { phase: 'reviewing', getIntensity: () => 0, onFinish: vi.fn(), onCancelRecording: vi.fn(), onAccept: vi.fn() },
    })
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with the voice-preferences-unavailable indicator shown', async () => {
    const { container } = renderComposer({ voicePreferencesUnavailable: true })
    expect(await axe(container)).toHaveNoViolations()
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
