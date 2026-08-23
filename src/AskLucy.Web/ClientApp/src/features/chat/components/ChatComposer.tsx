import {
  RiArticleLine,
  RiAttachment2,
  RiErrorWarningLine,
  RiFingerprintLine,
  RiInfinityLine,
  RiMicLine,
  RiMicOffLine,
  RiSendPlane2Fill,
} from '@remixicon/react'
import { Box, IconButton, Alert, Paper, Snackbar, Stack, TextField, Tooltip } from '@mui/material'
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
  /** Push-to-Talk supports two gestures on the same control (specs/034-transcription-crash-
   * gesture-and-continuous-view FR-004): a tap starts recording and shows explicit confirm/
   * discard controls (`recording.onFinish`/`onCancelRecording`), waiting for the user; a hold
   * shows only the waveform and `onStopCapture` is the sole, automatic trigger for finishing
   * once released — no button press needed for that path. */
  onStartCapture: () => void
  onStopCapture: () => void
  onClearCaptureError: () => void
  /** spec.md FR-080, User Story 5 — omitted (button hidden) when there's no active conversation yet. */
  onInsertPromptClick?: () => void
  /** specs/029-fix-chat-widget-bugs contracts/expanded-voice-control-consolidation.md —
   * switches between Continuous and Push-to-Talk, reachable from a small menu anchored to
   * the mic rather than a separate persistent icon (FR-006, Clarification Q3). Disabled
   * while a Push-to-Talk capture is in progress (same guard `VoiceControlBar.tsx` used). */
  onToggleMode: () => void
  /** Push-to-Talk's recording state — `phase`/`getIntensity` drive this component's own
   * visuals (waveform, disabled-while-transcribing). `onFinish`/`onCancelRecording` are the
   * same shared `VoiceControlsProps` contract `CollapsedVoiceControls` consumes for its own
   * click-to-record flow — here they're used specifically for a *tap*-started recording's
   * confirm/discard controls (specs/034); a *hold*-started recording never shows them and
   * finishes solely via `onStopCapture` on release. `undefined` (or `phase: 'idle'`) means no
   * recording is in progress. */
  recording?: {
    phase: RecordingPhase
    getIntensity: () => number
    onFinish: () => void
    onCancelRecording: () => void
  }
  /** specs/029-fix-chat-widget-bugs FR-001/FR-002, research.md Decision 3 — true when the
   * user's saved voice preferences failed to load (voice features are still fully usable on
   * defaults). Drives a small, dismissible-by-nature (icon + tooltip, no persistent banner)
   * indicator here in the mic's settings area, replacing the previous full-width Snackbar
   * that fired on every chat load regardless of whether this ever happened. */
  voicePreferencesUnavailable?: boolean
}

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
  voicePreferencesUnavailable,
}: ChatComposerProps) {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const { extractText } = usePdfTextExtraction()
  const prefersReducedMotion = usePrefersReducedMotion()

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

  // specs/034-transcription-crash-gesture-and-continuous-view FR-004/FR-005/FR-006/FR-007 —
  // two gestures on one control. A tap and a hold are physically identical at the moment of
  // press — only distinguishable by what happens next — so both start recording identically
  // and only resolve into "tap" (show confirm/discard, wait) or "hold" (auto-finish on release)
  // once release happens, based on elapsed hold duration (research.md Decision 3).
  //
  // `setPointerCapture` is the specs/033 bug fix, unchanged: without it, once the mic button's
  // visual state changes mid-press, a subsequent native `pointerup` can route to whatever
  // element is now under the pointer rather than this one, silently dropping the release.
  //
  // `isCapturingRef`, not the `isListening` prop, primarily gates the up-handlers: `isListening`
  // only updates once the parent re-renders after `onStartCapture()`'s async effects propagate
  // back down, which is not guaranteed to have happened yet by the time a fast real release
  // fires. The ref is set synchronously on down and is authoritative for "did *this* control
  // start the in-flight gesture," independent of parent re-render timing.
  // `recording?.phase === 'recording'` is checked too, defensively, so a hypothetical remount
  // while a recording is already active still lets release work correctly.
  const HOLD_THRESHOLD_MS = 350
  const isCapturingRef = useRef(false)
  const pressStartedAtRef = useRef(0)
  const isCapturing = () => isCapturingRef.current || recording?.phase === 'recording'

  const [isAwaitingTapReview, setIsAwaitingTapReview] = useState(false)

  const handleMicPointerDown = (event: React.PointerEvent<HTMLButtonElement>) => {
    if (isCapturing()) return
    isCapturingRef.current = true
    pressStartedAtRef.current = Date.now()
    setIsAwaitingTapReview(false)
    // Optional chaining: not implemented in jsdom (this codebase's test environment) and,
    // defensively, not guaranteed in every real environment either — Pointer Events Level 2 is
    // broadly supported, but the gesture still degrades acceptably without capture (the
    // fallback is the pre-specs/033 bug, not a crash).
    event.currentTarget.setPointerCapture?.(event.pointerId)
    onStartCapture()
  }

  const resolveGestureOnRelease = () => {
    if (!isCapturing()) return
    isCapturingRef.current = false
    const heldMs = Date.now() - pressStartedAtRef.current
    if (heldMs < HOLD_THRESHOLD_MS) {
      // Tap: recording continues; wait for an explicit Finish/Cancel instead of auto-completing.
      setIsAwaitingTapReview(true)
    } else {
      // Hold: release itself is the only trigger — finish immediately, no controls ever shown.
      onStopCapture()
    }
  }

  const handleMicPointerUp = () => resolveGestureOnRelease()

  const handleMicKeyDown: React.KeyboardEventHandler<HTMLButtonElement> = (event) => {
    if ((event.key !== ' ' && event.key !== 'Spacebar') || event.repeat) return
    event.preventDefault() // suppresses the native click-on-keyup a <button> fires for Space
    if (isCapturing()) return
    isCapturingRef.current = true
    pressStartedAtRef.current = Date.now()
    setIsAwaitingTapReview(false)
    onStartCapture()
  }

  const handleMicKeyUp: React.KeyboardEventHandler<HTMLButtonElement> = (event) => {
    if (event.key !== ' ' && event.key !== 'Spacebar') return
    event.preventDefault()
    resolveGestureOnRelease()
  }

  const handleTapReviewFinish = () => {
    setIsAwaitingTapReview(false)
    onStopCapture()
  }

  const handleTapReviewCancel = () => {
    setIsAwaitingTapReview(false)
    recording?.onCancelRecording()
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

  // Drives hiding the attach/insert-prompt/mode-switch controls during an active Push-to-Talk
  // recording (decluttering, unchanged from specs/031). Whether the mic button itself stays
  // mounted or gets swapped for RecordingReviewControls depends separately on
  // `isAwaitingTapReview` below (specs/034) — a hold never swaps it, a tap does once resolved.
  const isRecordingActive = Boolean(recording) && recording?.phase !== 'idle'
  const isModeSwitchBlocked = conversationMode === 'PushToTalk' && isListening

  return (
    <Box sx={{ p: 2, pt: 0 }}>
      <Paper
        variant="outlined"
        sx={{
          maxWidth: 800,
          mx: 'auto',
          borderRadius: `${radius.lg}px`,
          px: 1.5,
          py: 1,
          display: 'flex',
          flexDirection: 'column',
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

        {/* specs/030-composer-panel-refinements FR-001/FR-002 — text-entry region on top of
            a fixed footer row of controls, replacing the previous single-row pill layout. */}
        <Box>
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
            sx={{
              py: 1.25,
              // specs/030-composer-panel-refinements FR-004, research.md Decision 2 — a fixed
              // lineHeight (matching the body1 token's 1.55) makes the maxRows={6} cap
              // deterministic instead of depending on an inherited/ambient value; past this
              // point the input scrolls internally rather than growing the composer further.
              '& .MuiInputBase-input': {
                lineHeight: 1.55,
                overflowY: 'auto',
              },
            }}
          />
        </Box>

        <Stack
          direction="row"
          spacing={0.5}
          sx={{ alignItems: 'center', width: '100%', flexShrink: 0 }}
        >
          {/* specs/031-voice-controls-redesign FR-006/FR-008, research.md Decision 3 —
              hidden for the duration of an active Push-to-Talk recording so the footer
              shows only recording-relevant controls, not every control at once. */}
          {!isRecordingActive && (
            <>
              <Tooltip title="Attach file">
                <IconButton onClick={() => fileInputRef.current?.click()} aria-label="Attach file">
                  <RiAttachment2 />
                </IconButton>
              </Tooltip>

              {onInsertPromptClick && (
                <Tooltip title="Insert saved prompt">
                  <IconButton onClick={onInsertPromptClick} aria-label="Insert saved prompt">
                    <RiArticleLine />
                  </IconButton>
                </Tooltip>
              )}
            </>
          )}

          {/* FR-005/FR-019: live waveform alongside the mic while actively recording — nothing
              left to visualize once capture has stopped and transcription has begun. specs/033:
              this no longer replaces the mic button (that swap was the actual cause of the
              pointer-capture bug); it renders as a sibling, and the mic button itself stays the
              same element throughout press → recording → transcribing. */}
          {recording?.phase === 'recording' && (
            <Box sx={{ width: 64 }}>
              <VoiceAnalyzer state="listening" getIntensity={recording.getIntensity} />
            </Box>
          )}

          {/* specs/034-transcription-crash-gesture-and-continuous-view — a tap-resolved
              recording swaps the mic button for explicit confirm/discard controls (reusing
              CollapsedVoiceControls' already-correct pattern). A hold-resolved recording never
              reaches this branch — it finishes directly on release via onStopCapture, and the
              mic button (setPointerCapture-protected) stays the same element throughout. */}
          {isAwaitingTapReview && recording ? (
            <RecordingReviewControls
              phase={recording.phase}
              onFinish={handleTapReviewFinish}
              onCancelRecording={handleTapReviewCancel}
              placement="right"
            />
          ) : (
            <Tooltip title={isListening ? 'Stop voice input' : 'Start voice input'}>
              <IconButton
                onPointerDown={conversationMode === 'PushToTalk' ? handleMicPointerDown : undefined}
                onPointerUp={conversationMode === 'PushToTalk' ? handleMicPointerUp : undefined}
                onPointerLeave={conversationMode === 'PushToTalk' ? handleMicPointerUp : undefined}
                onPointerCancel={conversationMode === 'PushToTalk' ? handleMicPointerUp : undefined}
                onClick={conversationMode === 'PushToTalk' ? undefined : handleContinuousMicClick}
                onKeyDown={conversationMode === 'PushToTalk' ? handleMicKeyDown : undefined}
                onKeyUp={conversationMode === 'PushToTalk' ? handleMicKeyUp : undefined}
                disabled={recording?.phase === 'transcribing'}
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
            </Tooltip>
          )}

          {/* Mode-switch toggle, anchored to the mic — no separate always-visible mode icon
              (FR-006). Shows the *current* mode's icon; a single click switches directly to
              the other mode, no intermediate menu (specs/032 — the prior two-click dropdown
              is removed). Hidden while recording (specs/031-voice-controls-redesign
              FR-006/FR-008, research.md Decision 3) alongside attach/insert-prompt above. */}
          {!isRecordingActive && (
            <>
              <Tooltip
                title={
                  isModeSwitchBlocked
                    ? 'Release the microphone to switch modes'
                    : conversationMode === 'Continuous'
                      ? 'Switch to Push-to-Talk'
                      : 'Switch to Continuous Conversation'
                }
              >
                <span>
                  <IconButton
                    onClick={onToggleMode}
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

              {/* FR-001/FR-002, research.md Decision 3 — a small, non-blocking indicator
                  that saved voice preferences couldn't load (defaults are in effect;
                  nothing here is broken), replacing the previous full-width Snackbar that
                  fired on every chat load. Deliberately just an icon + tooltip, not a
                  dismiss-and-forget banner. */}
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
            </>
          )}

          <Box sx={{ flex: 1 }} />

          <Tooltip title="Send message">
            {/* MUI Tooltip cannot attach directly to a disabled element — same
                <span> wrapper pattern already used above for the mode-switch button. */}
            <span>
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
            </span>
          </Tooltip>
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
