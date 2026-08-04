import AllInclusiveIcon from '@mui/icons-material/AllInclusive'
import GraphicEqIcon from '@mui/icons-material/GraphicEq'
import MicOffIcon from '@mui/icons-material/MicOff'
import StopIcon from '@mui/icons-material/Stop'
import TouchAppIcon from '@mui/icons-material/TouchApp'
import VolumeOffIcon from '@mui/icons-material/VolumeOff'
import VolumeUpIcon from '@mui/icons-material/VolumeUp'
import { Alert, Box, IconButton, Stack, Tooltip } from '@mui/material'
import type { MicrophonePermissionState } from '../voice/useSpeechRecognition'

export interface VoiceControlBarProps {
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
}

/**
 * SPEC-013's restored voice control surface — mic status/toggle, mute, and mode switch,
 * driven directly by `useSpeechRecognition`'s `isListening`/`permissionState` and
 * `useVoiceOutput`'s `isSpeaking`/`isMuted` (contracts/voice-control-integration.md),
 * rather than the `useVoiceState` conversation-turn machine the original spec
 * 012-elevenlabs-voice-engine version of this component reflected. Markup, icons, and
 * keyboard-operability pattern (FR-010/FR-024) are unchanged from that version — only the
 * props feeding them.
 *
 * The mode toggle is disabled while a Push-to-Talk capture is actively in progress
 * (`isListening && conversationMode === 'PushToTalk'`) per Clarification Q4 — switching
 * mid-capture is blocked, not auto-finished or discarded (research.md Decision 6).
 */
export function VoiceControlBar({
  isAvailable,
  isListening,
  isSpeaking,
  isMuted,
  conversationMode,
  errorMessage,
  permissionState,
  onStart,
  onStop,
  onCancel,
  onStopSpeaking,
  onToggleMode,
  onToggleMute,
  onClearError,
}: VoiceControlBarProps) {
  if (!isAvailable) {
    return null
  }

  const isModeSwitchBlocked = isListening && conversationMode === 'PushToTalk'

  const handleMicClick = () => {
    if (isListening) {
      onStop()
    } else {
      onStart()
    }
  }

  return (
    <Stack
      direction="row"
      spacing={1}
      sx={{ alignItems: 'center', px: 1.5, py: errorMessage || isListening || isSpeaking ? 1 : 0 }}
    >
      <Tooltip title={isListening ? 'Stop listening' : 'Start voice input'}>
        <IconButton
          onClick={handleMicClick}
          aria-label={isListening ? 'Stop voice input' : 'Start voice input'}
          color={errorMessage ? 'error' : 'primary'}
        >
          {isListening ? <MicOffIcon /> : <GraphicEqIcon />}
        </IconButton>
      </Tooltip>

      {isListening && (
        <Tooltip title="Cancel — discard without sending">
          <IconButton onClick={onCancel} aria-label="Cancel voice input" color="default">
            <MicOffIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      )}

      {isSpeaking && (
        <Tooltip title="Stop the reply">
          <IconButton onClick={onStopSpeaking} aria-label="Stop AI reply" color="error">
            <StopIcon />
          </IconButton>
        </Tooltip>
      )}

      <Tooltip title={isMuted ? 'Unmute speaker output' : 'Mute speaker output'}>
        <IconButton onClick={onToggleMute} aria-label={isMuted ? 'Unmute' : 'Mute'}>
          {isMuted ? <VolumeOffIcon /> : <VolumeUpIcon />}
        </IconButton>
      </Tooltip>

      <Tooltip
        title={
          isModeSwitchBlocked
            ? 'Release the microphone to switch modes'
            : conversationMode === 'Continuous'
              ? 'Continuous Conversation Mode — click for Push-to-Talk'
              : 'Push-to-Talk Mode — click for Continuous Conversation'
        }
      >
        {/* span wrapper so the tooltip still shows while the button is disabled */}
        <span>
          <IconButton
            onClick={onToggleMode}
            disabled={isModeSwitchBlocked}
            aria-label={
              conversationMode === 'Continuous'
                ? 'Switch to Push-to-Talk mode'
                : 'Switch to Continuous Conversation mode'
            }
            size="small"
          >
            {conversationMode === 'Continuous' ? (
              <AllInclusiveIcon fontSize="small" />
            ) : (
              <TouchAppIcon fontSize="small" />
            )}
          </IconButton>
        </span>
      </Tooltip>

      {isListening && (
        <Box sx={{ color: 'text.secondary', fontSize: '0.875rem' }}>Listening…</Box>
      )}
      {!isListening && isSpeaking && (
        <Box sx={{ color: 'text.secondary', fontSize: '0.875rem' }}>Lucy is speaking…</Box>
      )}

      {permissionState === 'denied' && (
        <Alert severity="warning" variant="outlined" sx={{ py: 0 }}>
          Microphone access denied.
        </Alert>
      )}

      {errorMessage && (
        <Alert severity="error" variant="outlined" onClose={onClearError} sx={{ py: 0 }}>
          {errorMessage}
        </Alert>
      )}
    </Stack>
  )
}
