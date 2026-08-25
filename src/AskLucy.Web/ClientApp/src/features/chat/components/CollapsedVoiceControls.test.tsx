import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import { CollapsedVoiceControls, type VoiceControlsProps } from './CollapsedVoiceControls'

expect.extend(toHaveNoViolations)

function renderControls(overrides: Partial<VoiceControlsProps> = {}) {
  const props: VoiceControlsProps = {
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
  return { ...render(<CollapsedVoiceControls {...props} />), props }
}

describe('CollapsedVoiceControls (FR-003)', () => {
  it('renders Push-to-Talk, Continuous toggle, and Mute when idle', () => {
    renderControls()

    expect(screen.getByRole('button', { name: 'Start voice input' })).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Switch to Continuous Conversation mode' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Mute' })).toBeInTheDocument()
  })

  it('hides Push-to-Talk when in Continuous mode', () => {
    renderControls({ conversationMode: 'Continuous' })
    expect(screen.queryByRole('button', { name: 'Start voice input' })).not.toBeInTheDocument()
  })
})

// specs/031-voice-controls-redesign FR-001/FR-003, research.md Decision 1/7 — Finish now
// transcribes directly (no separate reviewing/accept phase); this shared component and
// `useVoiceRecorder`/`RecordingReviewControls`'s fix apply here automatically since
// `CollapsedVoiceControls` consumes the same contract as `ChatComposer`.
describe('CollapsedVoiceControls Push-to-Talk recording review (specs/026-floating-chat-assistant FR-020–FR-023)', () => {
  const recordingProps = (phase: 'recording' | 'transcribing') => ({
    recording: {
      phase,
      getIntensity: () => 0.5,
      onFinish: vi.fn(),
      onCancelRecording: vi.fn(),
    },
  })

  it('replaces the normal Push-to-Talk/Continuous/Mute stack with review controls while recording', () => {
    renderControls(recordingProps('recording'))

    expect(screen.getByRole('button', { name: 'Finished speaking' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel recording' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Start voice input' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Mute' })).not.toBeInTheDocument()
  })

  it('never shows a separate "send for transcription" control while recording or transcribing', () => {
    renderControls(recordingProps('recording'))
    expect(
      screen.queryByRole('button', { name: 'Send recording for transcription' }),
    ).not.toBeInTheDocument()

    renderControls(recordingProps('transcribing'))
    expect(
      screen.queryAllByRole('button', { name: 'Send recording for transcription' }),
    ).toHaveLength(0)
  })

  it('calls the recording callbacks — same accessible names FR-023 requires VoiceControlBar to share', () => {
    const { props } = renderControls(recordingProps('recording'))

    fireEvent.click(screen.getByRole('button', { name: 'Finished speaking' }))
    expect(props.recording?.onFinish).toHaveBeenCalledTimes(1)

    fireEvent.click(screen.getByRole('button', { name: 'Cancel recording' }))
    expect(props.recording?.onCancelRecording).toHaveBeenCalledTimes(1)
  })

  it('has no automatically detectable a11y violations while recording or transcribing', async () => {
    const recording = renderControls(recordingProps('recording'))
    expect(await axe(recording.container)).toHaveNoViolations()

    const transcribing = renderControls(recordingProps('transcribing'))
    expect(await axe(transcribing.container)).toHaveNoViolations()
  })
})

// specs/040-composer-interaction-bug-fixes US7 T023 — all tooltips must use bottom placement.
// All three previously used placement="left"; each is now placement="bottom". Hover triggers
// the MUI tooltip portal (role="tooltip"); jsdom cannot compute Popper.js placement, but
// tooltip content presence confirms placement="bottom" did not break tooltip display.
describe('CollapsedVoiceControls — bottom tooltip placement (specs/040 US7 — T023)', () => {
  it('mic tooltip appears on hover with expected label (T023)', async () => {
    const user = userEvent.setup()
    renderControls()
    await user.hover(screen.getByRole('button', { name: 'Start voice input' }))
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Push to talk')
  })

  it('mode-switch tooltip appears on hover with expected label (T023)', async () => {
    const user = userEvent.setup()
    renderControls()
    await user.hover(screen.getByRole('button', { name: 'Switch to Continuous Conversation mode' }))
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Switch to Continuous listening')
  })

  it('mute tooltip appears on hover with expected label (T023)', async () => {
    const user = userEvent.setup()
    renderControls()
    await user.hover(screen.getByRole('button', { name: 'Mute' }))
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Mute agent')
  })
})
