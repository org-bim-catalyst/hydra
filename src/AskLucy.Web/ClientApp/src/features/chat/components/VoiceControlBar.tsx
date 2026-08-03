import AllInclusiveIcon from '@mui/icons-material/AllInclusive'
import GraphicEqIcon from '@mui/icons-material/GraphicEq'
import MicOffIcon from '@mui/icons-material/MicOff'
import StopIcon from '@mui/icons-material/Stop'
import TouchAppIcon from '@mui/icons-material/TouchApp'
import VolumeOffIcon from '@mui/icons-material/VolumeOff'
import VolumeUpIcon from '@mui/icons-material/VolumeUp'
import { Alert, Box, IconButton, Stack, Tooltip } from '@mui/material'
import type { VoiceStateName } from '../voice/useVoiceState'

export interface VoiceControlBarProps {
  isAvailable: boolean
  voiceState: VoiceStateName
  errorMessage: string | null
  conversationMode: 'PushToTalk' | 'Continuous'
  isMuted: boolean
  onStart: () => void
  onCancelListening: () => void
  onStop: () => void
  onToggleMode: () => void
  onToggleMute: () => void
  onClearError: () => void
}

const STATE_LABEL: Partial<Record<VoiceStateName, string>> = {
  Listening: 'Listening for your voice…',
  UserSpeaking: "You're speaking…",
  Processing: 'Processing what you said…',
  AiThinking: 'Lucy is thinking…',
  AiSpeaking: 'Lucy is speaking…',
  Interrupted: 'Go ahead, listening…',
}

const SPEAKING_STATES: ReadonlySet<VoiceStateName> = new Set([
  'Processing', 'AiThinking', 'AiSpeaking', 'Interrupted',
])

/**
 * The consolidated voice control surface (US4, tasks.md T069/T070) — replaces the ad hoc
 * mic/mode-toggle controls added directly to `ChatComposer.tsx` during US1/US2 with a single,
 * dedicated, fully keyboard-operable component (FR-024) driven entirely by `useVoiceState`
 * (FR-020). Mic/mode/mute/stop are the same four control surfaces spec.md's Voice Controls
 * section describes.
 */
export function VoiceControlBar({
  isAvailable,
  voiceState,
  errorMessage,
  conversationMode,
  isMuted,
  onStart,
  onCancelListening,
  onStop,
  onToggleMode,
  onToggleMute,
  onClearError,
}: VoiceControlBarProps) {
  if (!isAvailable) {
    return null
  }

  const isIdle = voiceState === 'Idle'
  const isListeningPhase = voiceState === 'Listening' || voiceState === 'UserSpeaking'
  const isSpeakingPhase = SPEAKING_STATES.has(voiceState)

  const handleMicClick = () => {
    if (isIdle) {
      onStart()
    } else if (isListeningPhase) {
      onCancelListening()
    } else {
      onStop()
    }
  }

  return (
    <Stack direction="row" spacing={1} sx={{ alignItems: 'center', px: 1.5, py: voiceState === 'Error' || !isIdle ? 1 : 0 }}>
      <Tooltip title={isIdle ? 'Start a spoken conversation' : 'Stop voice conversation'}>
        <IconButton
          onClick={handleMicClick}
          aria-label={isIdle ? 'Start voice conversation' : 'Stop voice conversation'}
          color={voiceState === 'Error' ? 'error' : 'primary'}
        >
          {isIdle ? <GraphicEqIcon /> : <MicOffIcon />}
        </IconButton>
      </Tooltip>

      {isSpeakingPhase && (
        <Tooltip title="Stop the reply">
          <IconButton onClick={onStop} aria-label="Stop AI reply" color="error">
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
          conversationMode === 'Continuous'
            ? 'Continuous Conversation Mode — click for Push-to-Talk'
            : 'Push-to-Talk Mode — click for Continuous Conversation'
        }
      >
        <IconButton
          onClick={onToggleMode}
          aria-label={conversationMode === 'Continuous' ? 'Switch to Push-to-Talk mode' : 'Switch to Continuous Conversation mode'}
          size="small"
        >
          {conversationMode === 'Continuous' ? <AllInclusiveIcon fontSize="small" /> : <TouchAppIcon fontSize="small" />}
        </IconButton>
      </Tooltip>

      {!isIdle && voiceState !== 'Error' && (
        <Box sx={{ color: 'text.secondary', fontSize: '0.875rem' }}>{STATE_LABEL[voiceState] ?? voiceState}</Box>
      )}

      {voiceState === 'Error' && errorMessage && (
        <Alert severity="error" variant="outlined" onClose={onClearError} sx={{ py: 0 }}>
          {errorMessage}
        </Alert>
      )}
    </Stack>
  )
}
