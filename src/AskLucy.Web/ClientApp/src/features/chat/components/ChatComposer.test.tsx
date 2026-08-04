import { fireEvent, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ChatComposer, type ChatComposerProps } from './ChatComposer'

expect.extend(toHaveNoViolations)

const MIC_BUTTON_NAME = /^(start|stop) voice input$/i

function renderComposer(overrides: Partial<ChatComposerProps> = {}) {
  const props: ChatComposerProps = {
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
    onCancelCapture: vi.fn(),
    onClearCaptureError: vi.fn(),
    ...overrides,
  }
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
        value=""
        onChange={vi.fn()}
        onSend={vi.fn()}
        conversationMode="PushToTalk"
        isListening={true}
        permissionState="unknown"
        captureError={null}
        onStartCapture={props.onStartCapture}
        onStopCapture={props.onStopCapture}
        onCancelCapture={props.onCancelCapture}
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
        value=""
        onChange={vi.fn()}
        onSend={vi.fn()}
        conversationMode="PushToTalk"
        isListening={true}
        permissionState="unknown"
        captureError={null}
        onStartCapture={props.onStartCapture}
        onStopCapture={props.onStopCapture}
        onCancelCapture={props.onCancelCapture}
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

describe('ChatComposer — Cancel while listening (US2)', () => {
  it('shows a cancel control while listening that discards without sending', () => {
    const { props } = renderComposer({ isListening: true })
    fireEvent.click(screen.getByRole('button', { name: /cancel voice input/i }))
    expect(props.onCancelCapture).toHaveBeenCalledTimes(1)
  })

  it('does not show the cancel control while idle', () => {
    renderComposer({ isListening: false })
    expect(screen.queryByRole('button', { name: /cancel voice input/i })).not.toBeInTheDocument()
  })
})

describe('ChatComposer — Continuous mode (US2, FR-006)', () => {
  it('does not render an interactive mic button in Continuous mode (capture is already always-on)', () => {
    renderComposer({ conversationMode: 'Continuous' })
    expect(screen.queryByRole('button', { name: MIC_BUTTON_NAME })).not.toBeInTheDocument()
  })

  it('shows a listening indicator in Continuous mode while capture is active', () => {
    renderComposer({ conversationMode: 'Continuous', isListening: true })
    expect(screen.getByText(/listening/i)).toBeInTheDocument()
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

    rerender(
      <ChatComposer
        value=""
        onChange={vi.fn()}
        onSend={vi.fn()}
        conversationMode="PushToTalk"
        isListening={false}
        permissionState="granted"
        captureError={null}
        onStartCapture={vi.fn()}
        onStopCapture={vi.fn()}
        onCancelCapture={vi.fn()}
        onClearCaptureError={vi.fn()}
      />,
    )
    expect(screen.queryByText(/microphone access was denied/i)).not.toBeInTheDocument()
  })
})
