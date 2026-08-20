import {
  RiArticleLine,
  RiAttachment2,
  RiErrorWarningLine,
  RiFingerprintLine,
  RiInfinityLine,
  RiMicLine,
  RiMicOffLine,
  RiSendPlane2Fill,
  RiTranslate2,
  RiVolumeMuteLine,
  RiVolumeUpLine,
} from '@remixicon/react'
import { Box, IconButton, Menu, MenuItem, Alert, Paper, Snackbar, Stack, TextField, Tooltip } from '@mui/material'
import { useRef, useState } from 'react'
import { transcribeAudio } from '../api/aiApi'
import { usePdfTextExtraction } from '../pdf/usePdfTextExtraction'
import type { MicrophonePermissionState } from '../voice/useSpeechRecognition'
import type { RecordingPhase } from '../voice/useVoiceRecorder'
import { radius } from '../../../theme'
import { usePrefersReducedMotion } from '../../../hooks/usePrefersReducedMotion'
import { RecordingReviewControls } from './RecordingReviewControls'
import { VoiceAnalyzer } from './VoiceAnalyzer'

export interface ChatComposerProps {
  value: string
  onChange: (text: string) => void
  onSend: () => void
  disabled?: boolean
  /** SPEC-013 US2: which voice input mode is active — governs whether the mic is a manual
   * hold/toggle trigger (Push-to-Talk) or a simple listening on/off toggle (Continuous). */
  conversationMode: 'PushToTalk' | 'Continuous'
  isListening: boolean
  permissionState: MicrophonePermissionState
  captureError: string | null
  onStartCapture: () => void
  onStopCapture: () => void
  // No onCancelCapture: discovered during implementation to have no remaining distinct use
  // once the controls were actually consolidated — Push-to-Talk cancellation is owned by
  // `RecordingReviewControls`' own cancel button (rendered throughout `recording.phase !==
  // 'idle'`, including while still 'recording'), and Continuous mode has no separate
  // cancel concept beyond the mic's own start/stop toggle. The underlying
  // `recorder.cancel()`/`recognition.cancel()` capability is unaffected — only this now-
  // redundant prop is gone.
  onClearCaptureError: () => void
  /** spec.md FR-080, User Story 5 — omitted (button hidden) when there's no active conversation yet. */
  onInsertPromptClick?: () => void
  /** specs/029-fix-chat-widget-bugs contracts/expanded-voice-control-consolidation.md —
   * switches between Continuous and Push-to-Talk, reachable from a small menu anchored to
   * the mic rather than a separate persistent icon (FR-006, Clarification Q3). Disabled
   * while a Push-to-Talk capture is in progress (same guard `VoiceControlBar.tsx` used). */
  onToggleMode: () => void
  /** Push-to-Talk's record → review → cancel/accept flow, absorbed from the retired
   * `VoiceControlBar` (research.md Decision 5). `undefined` (or `phase: 'idle'`) renders
   * the plain mic button instead. */
  recording?: {
    phase: RecordingPhase
    getIntensity: () => number
    onFinish: () => void
    onCancelRecording: () => void
    onAccept: () => void
  }
  /** Speaker-output mute, merged with "stop the current reply" into one toggle
   * (FR-006a/FR-006b, research.md Decision 5a) — unrelated to the microphone. The caller
   * is responsible for also stopping in-progress speech when toggling to muted. */
  isMuted: boolean
  onToggleMute: () => void
  /** Relocated from the panel's top toolbar into this row (FR-007, research.md Decision 6). */
  onTranslateLastClick: () => void
  /** specs/029-fix-chat-widget-bugs FR-001/FR-002, research.md Decision 3 — true when the
   * user's saved voice preferences failed to load (voice features are still fully usable on
   * defaults). Drives a small, dismissible-by-nature (icon + tooltip, no persistent banner)
   * indicator here in the mic's settings area, replacing the previous full-width Snackbar
   * that fired on every chat load regardless of whether this ever happened. */
  voicePreferencesUnavailable?: boolean
}

const HOLD_THRESHOLD_MS = 350

/** File-attach dispatch by MIME type (PDF/audio/CSV) - preserved from the legacy app. Voice
 * input, mode-switching, and speaker-mute are handled by the controls below, driven by the
 * `useSpeechRecognition`/`useVoiceRecorder`/`useVoiceOutput` instances `ConversationView`
 * owns — this is the single consolidated voice-control surface for the Expanded chat panel
 * (specs/029-fix-chat-widget-bugs contracts/expanded-voice-control-consolidation.md;
 * `VoiceControlBar` no longer renders alongside this component). */
export function ChatComposer({
  value,
  onChange,
  onSend,
  disabled,
  conversationMode,
  isListening,
  permissionState,
  captureError,
  onStartCapture,
  onStopCapture,
  onClearCaptureError,
  onInsertPromptClick,
  onToggleMode,
  recording,
  isMuted,
  onToggleMute,
  onTranslateLastClick,
  voicePreferencesUnavailable,
}: ChatComposerProps) {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const { extractText } = usePdfTextExtraction()
  const prefersReducedMotion = usePrefersReducedMotion()
  const [modeMenuAnchor, setModeMenuAnchor] = useState<HTMLElement | null>(null)

  // Distinguishes a genuine *hold* (press, speak, release) from a *quick tap* meant to toggle
  // listening on and leave it on (Clarification Q1: both are supported on the same control).
  // Only relevant in Push-to-Talk — Continuous mode's mic is a plain toggle (FR-006).
  // A tap and a hold are physically the same down-then-up event pair - the only thing that
  // tells them apart is how long the control was held, so both the pointer and keyboard paths
  // below start capture on "down" and only decide whether to *also* stop it on "up" based on
  // elapsed time. A tap under the threshold leaves capture running (toggled on); the second,
  // separate tap that later turns it off is handled by the plain onClick toggle path.
  //
  // Set on down, cleared on up - `null` means "this up event doesn't belong to a down this
  // control initiated" (e.g. the second tap of a toggle-off sequence, where the down
  // deliberately no-ops below because capture is already running).
  const captureStartedAtRef = useRef<number | null>(null)
  // De-duplicates the synthetic `click` a browser fires after `pointerup`/`touchend` on the
  // same element (research.md Decision 5's flagged risk) - without this, a hold-release or a
  // toggle-start tap would be immediately followed by a third, spurious toggle via that click.
  // Keyboard/screen-reader activation never sets this (no pointer event precedes it), so the
  // toggle path below still works for those.
  const suppressNextClickRef = useRef(false)

  const handleFile = async (file: File) => {
    if (file.type === 'application/pdf') {
      const extracted = await extractText(file)
      onChange(`${value}${extracted}`)
    } else if (file.type.startsWith('audio/')) {
      const transcript = await transcribeAudio(file)
      onChange(`${value}${transcript}`)
    } else if (file.type === 'text/csv' || file.name.endsWith('.csv')) {
      const csvText = await file.text()
      onChange(`${value}${csvText}`)
    }
  }

  // Not a new capture if already listening (started by an earlier tap) - this "down" begins
  // the *second* tap that will turn it off via onClick below, not a new hold.
  const handleMicPointerDown = () => {
    if (isListening) return
    captureStartedAtRef.current = Date.now()
    onStartCapture()
  }

  const handleMicPointerUp = () => {
    // Not the "down" this control initiated (e.g. the toggle-off tap) - let onClick handle it.
    if (captureStartedAtRef.current === null) return
    const heldMs = Date.now() - captureStartedAtRef.current
    captureStartedAtRef.current = null
    suppressNextClickRef.current = true
    if (heldMs >= HOLD_THRESHOLD_MS) {
      onStopCapture() // genuine hold-and-release.
    }
    // else: a quick tap - leave capture running; a later, separate tap turns it off via onClick.
  }

  const handleMicClick = () => {
    if (suppressNextClickRef.current) {
      suppressNextClickRef.current = false
      return
    }
    // No preceding pointer event on *this* down-up pair - either a plain
    // click/tap/screen-reader activation, or the second tap of a toggle-off sequence (whose
    // pointerdown deliberately no-oped above since capture was already running).
    if (isListening) {
      onStopCapture()
    } else {
      onStartCapture()
    }
  }

  const handleMicKeyDown: React.KeyboardEventHandler<HTMLButtonElement> = (event) => {
    if ((event.key !== ' ' && event.key !== 'Spacebar') || event.repeat) return
    event.preventDefault() // suppresses the native click-on-keyup a <button> fires for Space
    if (isListening) return
    captureStartedAtRef.current = Date.now()
    onStartCapture()
  }

  const handleMicKeyUp: React.KeyboardEventHandler<HTMLButtonElement> = (event) => {
    if (event.key !== ' ' && event.key !== 'Spacebar') return
    event.preventDefault()
    if (captureStartedAtRef.current === null) return
    const heldMs = Date.now() - captureStartedAtRef.current
    captureStartedAtRef.current = null
    if (heldMs >= HOLD_THRESHOLD_MS) {
      onStopCapture()
    }
    // else: a quick press - leave capture running; the user presses Space again (or clicks)
    // later to turn it off.
  }

  // Continuous mode's mic is a plain listening on/off toggle — no hold-vs-tap gesture is
  // needed since there's no separate "review before sending" step to distinguish (FR-006).
  const handleContinuousMicClick = () => {
    if (isListening) {
      onStopCapture()
    } else {
      onStartCapture()
    }
  }

  const isRecordingReview = Boolean(recording) && recording?.phase !== 'idle'
  const isModeSwitchBlocked = conversationMode === 'PushToTalk' && isListening

  const handleToggleModeClick = () => {
    onToggleMode()
    setModeMenuAnchor(null)
  }

  return (
    <Box sx={{ p: 2, pt: 0 }}>
      <Paper
        variant="outlined"
        sx={{
          maxWidth: 800,
          mx: 'auto',
          borderRadius: `${radius.pill}px`,
          px: 1,
          minHeight: 56,
          display: 'flex',
          alignItems: 'center',
          transition: (theme) => theme.transitions.create(['border-color', 'box-shadow']),
          '&:focus-within': {
            borderColor: 'primary.main',
            boxShadow: (theme) => `0 0 0 3px ${theme.palette.primary.main}1f`,
          },
        }}
      >
        <input
          ref={fileInputRef}
          type="file"
          accept=".pdf,.csv,audio/*"
          hidden
          onChange={(e) => {
            const file = e.target.files?.[0]
            if (file) void handleFile(file)
            e.target.value = ''
          }}
        />
        <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', width: '100%' }}>
          <IconButton onClick={() => fileInputRef.current?.click()} aria-label="Attach file">
            <RiAttachment2 />
          </IconButton>

          {onInsertPromptClick && (
            <IconButton onClick={onInsertPromptClick} aria-label="Insert saved prompt">
              <RiArticleLine />
            </IconButton>
          )}

          {isRecordingReview && recording ? (
            <>
              {/* FR-005/FR-019: live waveform while actively recording — nothing left to
                  visualize once capture has stopped and review has begun. */}
              {recording.phase === 'recording' && (
                <Box sx={{ width: 64 }}>
                  <VoiceAnalyzer state="listening" getIntensity={recording.getIntensity} />
                </Box>
              )}
              <RecordingReviewControls
                phase={recording.phase}
                onFinish={recording.onFinish}
                onCancelRecording={recording.onCancelRecording}
                onAccept={recording.onAccept}
                placement="right"
              />
            </>
          ) : (
            <IconButton
              onPointerDown={conversationMode === 'PushToTalk' ? handleMicPointerDown : undefined}
              onPointerUp={conversationMode === 'PushToTalk' ? handleMicPointerUp : undefined}
              onPointerLeave={conversationMode === 'PushToTalk' ? handleMicPointerUp : undefined}
              onClick={conversationMode === 'PushToTalk' ? handleMicClick : handleContinuousMicClick}
              onKeyDown={conversationMode === 'PushToTalk' ? handleMicKeyDown : undefined}
              onKeyUp={conversationMode === 'PushToTalk' ? handleMicKeyUp : undefined}
              aria-label={isListening ? 'Stop voice input' : 'Start voice input'}
              color={isListening ? 'secondary' : 'default'}
              sx={
                isListening && !prefersReducedMotion
                  ? {
                      animation: 'ask-lucy-mic-pulse 1.4s ease-in-out infinite',
                      '@keyframes ask-lucy-mic-pulse': {
                        '0%, 100%': { opacity: 1 },
                        '50%': { opacity: 0.4 },
                      },
                    }
                  : undefined
              }
            >
              {isListening ? <RiMicOffLine /> : <RiMicLine />}
            </IconButton>
          )}

          {/* Mode-switch menu, anchored to the mic — no separate always-visible mode icon
              (FR-006). Shows the *current* mode's icon; opens a menu offering the other. */}
          <Tooltip
            title={
              isModeSwitchBlocked
                ? 'Release the microphone to switch modes'
                : conversationMode === 'Continuous'
                  ? 'Continuous Conversation Mode — click for options'
                  : 'Push-to-Talk Mode — click for options'
            }
          >
            <span>
              <IconButton
                onClick={(e) => setModeMenuAnchor(e.currentTarget)}
                disabled={isModeSwitchBlocked}
                aria-label="Voice input mode settings"
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
          <Menu anchorEl={modeMenuAnchor} open={Boolean(modeMenuAnchor)} onClose={() => setModeMenuAnchor(null)}>
            <MenuItem onClick={handleToggleModeClick}>
              {conversationMode === 'Continuous' ? 'Switch to Push-to-Talk' : 'Switch to Continuous Conversation'}
            </MenuItem>
          </Menu>

          {/* FR-001/FR-002, research.md Decision 3 — a small, non-blocking indicator that
              saved voice preferences couldn't load (defaults are in effect; nothing here is
              broken), replacing the previous full-width Snackbar that fired on every chat
              load. Deliberately just an icon + tooltip, not a dismiss-and-forget banner. */}
          {voicePreferencesUnavailable && (
            <Tooltip title="Using default voice settings — couldn't load your saved preferences">
              <RiErrorWarningLine
                aria-label="Voice preferences unavailable, using defaults"
                role="img"
                size={16}
                style={{ opacity: 0.6, flexShrink: 0 }}
              />
            </Tooltip>
          )}

          {/* Speaker-output mute, merged with "stop the current reply" (FR-006a/FR-006b) —
              a single always-visible toggle, unrelated to and visually distinct from the
              mic. Not folded into the mode menu above. */}
          <Tooltip title={isMuted ? 'Unmute Lucy' : 'Mute Lucy'}>
            <IconButton onClick={onToggleMute} aria-label={isMuted ? 'Unmute Lucy' : 'Mute Lucy'}>
              {isMuted ? <RiVolumeMuteLine /> : <RiVolumeUpLine />}
            </IconButton>
          </Tooltip>

          <Tooltip title="Translate last response">
            <IconButton onClick={onTranslateLastClick} aria-label="Translate last response">
              <RiTranslate2 />
            </IconButton>
          </Tooltip>

          <TextField
            fullWidth
            multiline
            maxRows={6}
            variant="standard"
            placeholder="Message Ask Lucy..."
            value={value}
            onChange={(e) => onChange(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault()
                onSend()
              }
            }}
            disabled={disabled}
            slotProps={{ input: { disableUnderline: true } }}
            sx={{ py: 1.25 }}
          />
          <IconButton
            onClick={onSend}
            disabled={disabled || !value.trim()}
            aria-label="Send message"
            sx={{
              bgcolor: value.trim() && !disabled ? 'primary.main' : 'transparent',
              color: value.trim() && !disabled ? 'primary.contrastText' : 'text.disabled',
              '&:hover': { bgcolor: value.trim() && !disabled ? 'primary.dark' : 'action.hover' },
              transition: (theme) => theme.transitions.create(['background-color', 'color']),
            }}
          >
            <RiSendPlane2Fill size={20} />
          </IconButton>
        </Stack>
      </Paper>
      {permissionState === 'denied' && (
        <Box sx={{ maxWidth: 800, mx: 'auto', mt: 1 }}>
          <Alert severity="warning" variant="outlined">
            Microphone access was denied. Check your browser’s site permissions and try again.
          </Alert>
        </Box>
      )}
      <Snackbar open={Boolean(captureError)} autoHideDuration={5000} onClose={onClearCaptureError}>
        <Alert severity="error" variant="filled" onClose={onClearCaptureError}>
          {captureError}
        </Alert>
      </Snackbar>
    </Box>
  )
}
