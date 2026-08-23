import {
  RiFingerprintLine,
  RiInfinityLine,
  RiMicLine,
  RiMicOffLine,
  RiVolumeMuteLine,
  RiVolumeUpLine,
} from '@remixicon/react'
import { IconButton, Stack, Tooltip } from '@mui/material'
import { CIRCULAR_ACTION_CHROME } from '../../../components/workspace-shell/CircularAction'
import type { MicrophonePermissionState } from '../voice/useSpeechRecognition'
import type { RecordingPhase } from '../voice/useVoiceRecorder'
import { RecordingReviewControls } from './RecordingReviewControls'

/** specs/026-floating-chat-assistant contracts/chat-widget-components.md — the same data
 * contract the Expanded panel's `ChatComposer` consumes (specs/029-fix-chat-widget-bugs
 * research.md Decision 5 retired the separate `VoiceControlBar` this used to describe;
 * `ChatComposer` reads this shape directly now), shared between the Collapsed vertical
 * layout (this component) and the Expanded layout, so a behavior change only ever needs to
 * be made once (research.md #10). */
export interface VoiceControlsProps {
  isAvailable: boolean
  isListening: boolean
  isSpeaking: boolean
  isMuted: boolean
  conversationMode: 'PushToTalk' | 'Continuous'
  errorMessage: string | null
  permissionState: MicrophonePermissionState
  onStart: () => void
  onStop: () => void
  onCancel: () => void
  onStopSpeaking: () => void
  onToggleMode: () => void
  onToggleMute: () => void
  onClearError: () => void
  /** Push-to-Talk's record → stop-and-transcribe → cancel flow (FR-019–FR-023,
   * specs/031-voice-controls-redesign research.md Decision 1 — `onFinish` now transcribes
   * directly, no separate accept step). `undefined` (or `phase: 'idle'`) renders nothing
   * extra. */
  recording?: {
    phase: RecordingPhase
    getIntensity: () => number
    onFinish: () => void
    onCancelRecording: () => void
  }
}

/** Vertical icon-stack presentation of {@link VoiceControlsProps} for the Collapsed
 * widget (FR-003) — Push-to-Talk, Continuous Listening toggle, Mute Agent, plus the
 * finish/cancel/send review controls once a Push-to-Talk recording is underway. */
export function CollapsedVoiceControls({
  isListening,
  isMuted,
  conversationMode,
  onStart,
  onStop,
  onToggleMode,
  onToggleMute,
  recording,
}: VoiceControlsProps) {
  const handleMicClick = () => {
    if (isListening) {
      onStop()
    } else {
      onStart()
    }
  }

  if (recording && recording.phase !== 'idle') {
    return (
      <Stack spacing={1} sx={{ alignItems: 'center' }}>
        {/* FR-019: the live waveform itself is CollapsedChatControl's own VoiceAnalyzer
            (fed by the same recorder.getIntensity while recording — T045); this stack
            only owns the finish/cancel/send controls beneath it. */}
        <RecordingReviewControls
          phase={recording.phase}
          onFinish={recording.onFinish}
          onCancelRecording={recording.onCancelRecording}
          placement="left"
        />
      </Stack>
    )
  }

  return (
    <Stack spacing={1} sx={{ alignItems: 'center' }}>
      {conversationMode === 'PushToTalk' && (
        <Tooltip title={isListening ? 'Stop' : 'Push to talk'} placement="left">
          <IconButton
            onClick={handleMicClick}
            aria-label={isListening ? 'Stop voice input' : 'Start voice input'}
            size="small"
            sx={{ color: isListening ? 'secondary.main' : CIRCULAR_ACTION_CHROME.icon }}
          >
            {isListening ? <RiMicOffLine fontSize="small" /> : <RiMicLine fontSize="small" />}
          </IconButton>
        </Tooltip>
      )}
      <Tooltip
        title={
          conversationMode === 'Continuous'
            ? 'Continuous listening — switch to Push-to-Talk'
            : 'Switch to Continuous listening'
        }
        placement="left"
      >
        <IconButton
          onClick={onToggleMode}
          aria-label={
            conversationMode === 'Continuous'
              ? 'Switch to Push-to-Talk mode'
              : 'Switch to Continuous Conversation mode'
          }
          size="small"
          sx={{
            color:
              conversationMode === 'Continuous' ? 'secondary.main' : CIRCULAR_ACTION_CHROME.icon,
          }}
        >
          {conversationMode === 'Continuous' ? (
            <RiInfinityLine fontSize="small" />
          ) : (
            <RiFingerprintLine fontSize="small" />
          )}
        </IconButton>
      </Tooltip>
      <Tooltip title={isMuted ? 'Unmute agent' : 'Mute agent'} placement="left">
        <IconButton
          onClick={onToggleMute}
          aria-label={isMuted ? 'Unmute' : 'Mute'}
          size="small"
          sx={{ color: isMuted ? 'error.main' : CIRCULAR_ACTION_CHROME.icon }}
        >
          {isMuted ? <RiVolumeMuteLine fontSize="small" /> : <RiVolumeUpLine fontSize="small" />}
        </IconButton>
      </Tooltip>
    </Stack>
  )
}
