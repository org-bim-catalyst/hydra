import {
  RiFingerprintLine,
  RiInfinityLine,
  RiMicLine,
  RiMicOffLine,
  RiStopCircleLine,
  RiVolumeMuteLine,
  RiVolumeUpLine,
} from '@remixicon/react'
import { Alert, Box, IconButton, Stack, Tooltip } from '@mui/material'
import { RecordingReviewControls } from './RecordingReviewControls'
import { VoiceAnalyzer } from './VoiceAnalyzer'
import type { VoiceControlsProps } from './CollapsedVoiceControls'

export type VoiceControlBarProps = VoiceControlsProps

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
 *
 * specs/026-floating-chat-assistant research.md #10: now shares `VoiceControlsProps`
 * with `CollapsedVoiceControls` — this is the horizontal-layout half of that single data
 * contract, including the `recording` review flow (FR-020–FR-023), which renders
 * identically here as it does in the Collapsed widget.
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
  recording,
}: VoiceControlBarProps) {
  if (!isAvailable) {
    return null
  }

  const isModeSwitchBlocked = isListening && conversationMode === 'PushToTalk'
  const isRecordingReview = recording && recording.phase !== 'idle'

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
      sx={{
        alignItems: 'center',
        px: 1.5,
        py: errorMessage || isListening || isSpeaking || isRecordingReview ? 1 : 0,
      }}
    >
      {isRecordingReview && recording ? (
        <>
          {/* FR-019: live waveform while actively recording — nothing left to visualize
              once capture has stopped and review has begun. */}
          {recording.phase === 'recording' && (
            <Box sx={{ width: 96 }}>
              <VoiceAnalyzer state="listening" getIntensity={recording.getIntensity} />
            </Box>
          )}
          <RecordingReviewControls
            phase={recording.phase}
            onFinish={recording.onFinish}
            onCancelRecording={recording.onCancelRecording}
            onAccept={recording.onAccept}
            placement="left"
          />
          <Box sx={{ color: 'text.secondary', fontSize: '0.875rem' }}>
            {recording.phase === 'recording'
              ? 'Recording…'
              : recording.phase === 'reviewing'
                ? 'Review before sending'
                : 'Transcribing…'}
          </Box>
        </>
      ) : (
        <>
          <Tooltip title={isListening ? 'Stop listening' : 'Start voice input'}>
            <IconButton
              onClick={handleMicClick}
              aria-label={isListening ? 'Stop voice input' : 'Start voice input'}
              color={errorMessage ? 'error' : 'primary'}
            >
              {isListening ? <RiMicOffLine /> : <RiMicLine />}
            </IconButton>
          </Tooltip>

          {isListening && (
            <Tooltip title="Cancel — discard without sending">
              <IconButton onClick={onCancel} aria-label="Cancel voice input" color="default">
                <RiMicOffLine size={20} />
              </IconButton>
            </Tooltip>
          )}
        </>
      )}

      {isSpeaking && (
        <Tooltip title="Stop the reply">
          <IconButton onClick={onStopSpeaking} aria-label="Stop AI reply" color="error">
            <RiStopCircleLine />
          </IconButton>
        </Tooltip>
      )}

      <Tooltip title={isMuted ? 'Unmute speaker output' : 'Mute speaker output'}>
        <IconButton onClick={onToggleMute} aria-label={isMuted ? 'Unmute' : 'Mute'}>
          {isMuted ? <RiVolumeMuteLine /> : <RiVolumeUpLine />}
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
              <RiInfinityLine fontSize="small" />
            ) : (
              <RiFingerprintLine fontSize="small" />
            )}
          </IconButton>
        </span>
      </Tooltip>

      {!isRecordingReview && isListening && (
        <Box sx={{ color: 'secondary.main', fontSize: '0.875rem', fontWeight: 500 }}>
          Listening…
        </Box>
      )}
      {!isListening && !isRecordingReview && isSpeaking && (
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
