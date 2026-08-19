import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { CollapsedChatControl } from './CollapsedChatControl'
import type { VoiceControlsProps } from './CollapsedVoiceControls'

const baseVoiceControls: VoiceControlsProps = {
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
}

describe('CollapsedChatControl', () => {
  it('renders exactly the handle, analyzer, voice controls, and status label (FR-003 AC2)', () => {
    render(
      <CollapsedChatControl
        onExpand={() => {}}
        analyzerState="idle"
        getIntensity={() => 0}
        voiceControls={baseVoiceControls}
        contentId="ask-lucy-assistant-content"
      />,
    )

    expect(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' })).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Voice status: idle' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Start voice input' })).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Switch to Continuous Conversation mode' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Mute' })).toBeInTheDocument()
    expect(screen.getByText('IDLE')).toBeInTheDocument()

    // FR-003: nothing else — no message list, no text input.
    expect(screen.queryByPlaceholderText(/message/i)).not.toBeInTheDocument()
  })

  it('calls onExpand when the handle is activated', async () => {
    const onExpand = vi.fn()
    render(
      <CollapsedChatControl
        onExpand={onExpand}
        analyzerState="idle"
        getIntensity={() => 0}
        voiceControls={baseVoiceControls}
        contentId="ask-lucy-assistant-content"
      />,
    )
    screen.getByRole('button', { name: 'Expand Ask Lucy assistant' }).click()
    expect(onExpand).toHaveBeenCalledTimes(1)
  })

  it('exposes aria-expanded=false and aria-controls on the handle (research.md #9)', () => {
    render(
      <CollapsedChatControl
        onExpand={() => {}}
        analyzerState="idle"
        getIntensity={() => 0}
        voiceControls={baseVoiceControls}
        contentId="ask-lucy-assistant-content"
      />,
    )
    const handle = screen.getByRole('button', { name: 'Expand Ask Lucy assistant' })
    expect(handle).toHaveAttribute('aria-expanded', 'false')
    expect(handle).toHaveAttribute('aria-controls', 'ask-lucy-assistant-content')
  })
})
