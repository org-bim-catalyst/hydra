import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { describe, expect, it, vi } from 'vitest'
import { CollapsedChatControl } from './CollapsedChatControl'
import type { VoiceControlsProps } from './CollapsedVoiceControls'

expect.extend(toHaveNoViolations)

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

describe('CollapsedChatControl accessibility (research.md #9 — independent of CircularAction)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { container } = render(
      <CollapsedChatControl
        onExpand={() => {}}
        analyzerState="listening"
        getIntensity={() => 0.5}
        voiceControls={baseVoiceControls}
        contentId="ask-lucy-assistant-content"
      />,
    )
    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
