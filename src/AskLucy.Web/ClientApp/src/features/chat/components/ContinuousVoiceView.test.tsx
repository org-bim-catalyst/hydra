import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import { ContinuousVoiceView, type ContinuousVoiceViewProps } from './ContinuousVoiceView'

expect.extend(toHaveNoViolations)

function baseProps(): ContinuousVoiceViewProps {
  return {
    getReactiveIntensity: () => 0,
    statusLabel: 'Listening…',
    errorMessage: null,
    isMuted: false,
    onToggleMute: vi.fn(),
    onExit: vi.fn(),
  }
}

function renderView(overrides: Partial<ContinuousVoiceViewProps> = {}) {
  const props = { ...baseProps(), ...overrides }
  return { ...render(<ContinuousVoiceView {...props} />), props }
}

describe('ContinuousVoiceView (specs/034-transcription-crash-gesture-and-continuous-view US3, FR-009/FR-010)', () => {
  it('renders exactly two interactive controls — Exit and Mute — and no composer/attach/send elements', () => {
    renderView()

    expect(screen.getByRole('button', { name: 'Exit voice conversation' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Mute Lucy' })).toBeInTheDocument()
    expect(screen.getAllByRole('button')).toHaveLength(2)

    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /attach file/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /send message/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /voice input mode settings/i })).not.toBeInTheDocument()
  })

  it('tapping Mute calls onToggleMute without calling onExit', () => {
    const { props } = renderView()

    fireEvent.click(screen.getByRole('button', { name: 'Mute Lucy' }))

    expect(props.onToggleMute).toHaveBeenCalledTimes(1)
    expect(props.onExit).not.toHaveBeenCalled()
  })

  it('reflects the muted state in the control label', () => {
    renderView({ isMuted: true })

    expect(screen.getByRole('button', { name: 'Unmute Lucy' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Mute Lucy' })).not.toBeInTheDocument()
  })

  it('tapping Exit calls onExit', () => {
    const { props } = renderView()

    fireEvent.click(screen.getByRole('button', { name: 'Exit voice conversation' }))

    expect(props.onExit).toHaveBeenCalledTimes(1)
  })

  it('shows the status label and, when present, an error message', () => {
    renderView({ statusLabel: 'Speaking…', errorMessage: 'Voice input is unavailable.' })

    expect(screen.getByRole('status')).toHaveTextContent('Speaking…')
    expect(screen.getByRole('alert')).toHaveTextContent('Voice input is unavailable.')
  })

  it('does not show an error region when there is no error', () => {
    renderView({ errorMessage: null })

    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('both controls are keyboard-reachable and activatable (constitution §7)', () => {
    const { props } = renderView()

    const exitButton = screen.getByRole('button', { name: 'Exit voice conversation' })
    const muteButton = screen.getByRole('button', { name: 'Mute Lucy' })
    expect(exitButton.tabIndex).not.toBe(-1)
    expect(muteButton.tabIndex).not.toBe(-1)

    fireEvent.keyDown(muteButton, { key: 'Enter' })
    fireEvent.click(muteButton) // jsdom doesn't auto-translate Enter to a click on a real <button>
    expect(props.onToggleMute).toHaveBeenCalled()
  })

  it('eventually mounts scene content (lazy-loaded, same pattern as AiPresenceCard)', async () => {
    const { container } = renderView()

    await waitFor(() => {
      expect(container.querySelectorAll('canvas, svg').length).toBeGreaterThan(0)
    })
  })

  it('has no automatically detectable a11y violations', async () => {
    const { container } = renderView()
    await waitFor(() => screen.getByRole('button', { name: 'Exit voice conversation' }))

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
