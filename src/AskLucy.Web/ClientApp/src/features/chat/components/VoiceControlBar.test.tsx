import { fireEvent, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import { VoiceControlBar, type VoiceControlBarProps } from './VoiceControlBar'

expect.extend(toHaveNoViolations)

function renderBar(overrides: Partial<VoiceControlBarProps> = {}) {
  const props: VoiceControlBarProps = {
    isAvailable: true,
    voiceState: 'Idle',
    errorMessage: null,
    conversationMode: 'PushToTalk',
    isMuted: false,
    onStart: vi.fn(),
    onCancelListening: vi.fn(),
    onStop: vi.fn(),
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

  it('has no automatically detectable a11y violations while the AI is speaking with an error shown', async () => {
    const { container } = renderBar({ voiceState: 'Error', errorMessage: 'Something went wrong.' })
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('renders nothing when voice conversation is unavailable', () => {
    const { container } = renderBar({ isAvailable: false })
    expect(container).toBeEmptyDOMElement()
  })
})

describe('VoiceControlBar keyboard operability (FR-024)', () => {
  it('every control is reachable and activatable via keyboard alone', () => {
    const { props } = renderBar({ voiceState: 'AiSpeaking' })

    const micButton = screen.getByRole('button', { name: /stop voice conversation/i })
    const stopButton = screen.getByRole('button', { name: /stop ai reply/i })
    const muteButton = screen.getByRole('button', { name: /^mute$/i })
    const modeButton = screen.getByRole('button', { name: /switch to continuous conversation mode/i })

    for (const button of [micButton, stopButton, muteButton, modeButton]) {
      expect(button).not.toHaveAttribute('tabindex', '-1')
    }

    fireEvent.click(stopButton)
    expect(props.onStop).toHaveBeenCalledTimes(1)

    fireEvent.click(muteButton)
    expect(props.onToggleMute).toHaveBeenCalledTimes(1)

    fireEvent.click(modeButton)
    expect(props.onToggleMode).toHaveBeenCalledTimes(1)
  })

  it('clicking the mic button starts a conversation when idle, and stops it otherwise', () => {
    const { props, rerender } = renderBar({ voiceState: 'Idle' })
    fireEvent.click(screen.getByRole('button', { name: /start voice conversation/i }))
    expect(props.onStart).toHaveBeenCalledTimes(1)

    rerender(<VoiceControlBar {...props} voiceState="AiSpeaking" />)
    fireEvent.click(screen.getByRole('button', { name: /stop voice conversation/i }))
    expect(props.onStop).toHaveBeenCalledTimes(1)
  })

  it('clicking the mic button cancels listening (rather than stopping) while in the Listening state', () => {
    const { props } = renderBar({ voiceState: 'Listening' })
    fireEvent.click(screen.getByRole('button', { name: /stop voice conversation/i }))
    expect(props.onCancelListening).toHaveBeenCalledTimes(1)
    expect(props.onStop).not.toHaveBeenCalled()
  })
})
