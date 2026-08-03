import { fireEvent, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import { VoiceControlBar, type VoiceControlBarProps } from './VoiceControlBar'

expect.extend(toHaveNoViolations)

function renderBar(overrides: Partial<VoiceControlBarProps> = {}) {
  const props: VoiceControlBarProps = {
    isAvailable: true,
    isListening: false,
    isSpeaking: false,
    isMuted: false,
    conversationMode: 'PushToTalk',
    errorMessage: null,
    permissionState: 'unknown',
    onStart: vi.fn(),
    onStop: vi.fn(),
    onCancel: vi.fn(),
    onStopSpeaking: vi.fn(),
    onToggleMode: vi.fn(),
    onToggleMute: vi.fn(),
    onClearError: vi.fn(),
    ...overrides,
  }
  return { ...render(<VoiceControlBar {...props} />), props }
}

describe('VoiceControlBar accessibility (constitution §7/§10, FR-024)', () => {
  it('has no automatically detectable a11y violations when idle', async () => {
    const { container } = renderBar()
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations while listening', async () => {
    const { container } = renderBar({ isListening: true })
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations while the AI is speaking with an error shown', async () => {
    const { container } = renderBar({ isSpeaking: true, errorMessage: 'Something went wrong.' })
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in both muted and unmuted states', async () => {
    const muted = renderBar({ isMuted: true })
    expect(await axe(muted.container)).toHaveNoViolations()

    const unmuted = renderBar({ isMuted: false })
    expect(await axe(unmuted.container)).toHaveNoViolations()
  })

  it('renders nothing when voice is unavailable', () => {
    const { container } = renderBar({ isAvailable: false })
    expect(container).toBeEmptyDOMElement()
  })
})

describe('VoiceControlBar keyboard operability (FR-010/FR-024)', () => {
  it('every control is reachable and activatable via keyboard alone', () => {
    const { props } = renderBar({ isSpeaking: true })

    const micButton = screen.getByRole('button', { name: /start voice input/i })
    const stopButton = screen.getByRole('button', { name: /stop ai reply/i })
    const muteButton = screen.getByRole('button', { name: /^mute$/i })
    const modeButton = screen.getByRole('button', {
      name: /switch to continuous conversation mode/i,
    })

    for (const button of [micButton, stopButton, muteButton, modeButton]) {
      expect(button).not.toHaveAttribute('tabindex', '-1')
    }

    fireEvent.click(stopButton)
    expect(props.onStopSpeaking).toHaveBeenCalledTimes(1)

    fireEvent.click(muteButton)
    expect(props.onToggleMute).toHaveBeenCalledTimes(1)

    fireEvent.click(modeButton)
    expect(props.onToggleMode).toHaveBeenCalledTimes(1)
  })

  it('clicking the mic control starts listening when idle, and stops it while listening', () => {
    const { props, rerender } = renderBar({ isListening: false })
    fireEvent.click(screen.getByRole('button', { name: /start voice input/i }))
    expect(props.onStart).toHaveBeenCalledTimes(1)

    rerender(<VoiceControlBar {...props} isListening={true} />)
    fireEvent.click(screen.getByRole('button', { name: /stop voice input/i }))
    expect(props.onStop).toHaveBeenCalledTimes(1)
  })

  it('shows a separate cancel control while listening that discards without sending', () => {
    const { props } = renderBar({ isListening: true })
    fireEvent.click(screen.getByRole('button', { name: /cancel voice input/i }))
    expect(props.onCancel).toHaveBeenCalledTimes(1)
    expect(props.onStop).not.toHaveBeenCalled()
  })

  it('does not show the cancel control while idle', () => {
    renderBar({ isListening: false })
    expect(screen.queryByRole('button', { name: /cancel voice input/i })).not.toBeInTheDocument()
  })
})

describe('VoiceControlBar mute control (US1, FR-001/FR-003)', () => {
  it('swaps the icon/aria-label between Mute and Unmute based on isMuted', () => {
    const { rerender, props } = renderBar({ isMuted: false })
    expect(screen.getByRole('button', { name: /^mute$/i })).toBeInTheDocument()

    rerender(<VoiceControlBar {...props} isMuted={true} />)
    expect(screen.getByRole('button', { name: /^unmute$/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^mute$/i })).not.toBeInTheDocument()
  })

  it('calls onToggleMute when the mute control is activated via keyboard alone', () => {
    const { props } = renderBar({ isMuted: false })
    const muteButton = screen.getByRole('button', { name: /^mute$/i })
    muteButton.focus()
    expect(muteButton).toHaveFocus()

    fireEvent.keyDown(muteButton, { key: 'Enter', code: 'Enter' })
    fireEvent.click(muteButton) // jsdom does not synthesize the browser's Enter->click activation
    expect(props.onToggleMute).toHaveBeenCalledTimes(1)
  })
})

describe('VoiceControlBar mode-switch guard (Clarification Q4, research.md Decision 6)', () => {
  it('disables the mode toggle while a Push-to-Talk capture is actively in progress', () => {
    const { props } = renderBar({ conversationMode: 'PushToTalk', isListening: true })
    const modeButton = screen.getByRole('button', {
      name: /switch to continuous conversation mode/i,
    })
    expect(modeButton).toBeDisabled()

    fireEvent.click(modeButton)
    expect(props.onToggleMode).not.toHaveBeenCalled()
  })

  it('re-enables the mode toggle the instant listening stops', () => {
    const { rerender, props } = renderBar({ conversationMode: 'PushToTalk', isListening: true })
    rerender(<VoiceControlBar {...props} isListening={false} />)

    const modeButton = screen.getByRole('button', {
      name: /switch to continuous conversation mode/i,
    })
    expect(modeButton).not.toBeDisabled()

    fireEvent.click(modeButton)
    expect(props.onToggleMode).toHaveBeenCalledTimes(1)
  })

  it('does not disable the mode toggle while listening in Continuous mode (only Push-to-Talk blocks it)', () => {
    renderBar({ conversationMode: 'Continuous', isListening: true })
    const modeButton = screen.getByRole('button', { name: /switch to push-to-talk mode/i })
    expect(modeButton).not.toBeDisabled()
  })
})

describe('VoiceControlBar microphone permission (FR-009)', () => {
  it('shows a visible warning when microphone permission was denied', () => {
    renderBar({ permissionState: 'denied' })
    expect(screen.getByText(/microphone access denied/i)).toBeInTheDocument()
  })
})
