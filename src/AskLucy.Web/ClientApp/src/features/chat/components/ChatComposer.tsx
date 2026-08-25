import {
  RiAttachment2,
  RiErrorWarningLine,
  RiMicFill,
  RiMicLine,
  RiMicOffLine,
  RiSendPlane2Fill,
  RiStopLine,
  RiVoiceprintLine,
} from '@remixicon/react'
import { Box, IconButton, Alert, Paper, Snackbar, Stack, TextField, Tooltip } from '@mui/material'
import { useEffect, useRef, useState } from 'react'
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
  /** specs/039-composer-interaction-states-redesign — a single click on this control both
   * switches the persisted voice-mode preference and starts/stops listening in the same
   * action; the caller (`ChatPage.tsx`) pairs this with `onStartCapture`/`onStopCapture` and
   * awaits the preference save (contracts/composer-voice-states.md). `ChatComposer` only
   * calls this and renders the correct icon for the current mode/state — it does not need to
   * know the pairing or ordering happens. Rendered as the continuous-conversation entry
   * action (`voiceprint-line`) in the empty state, or the exit action (`stop-line`) while
   * already in Continuous mode. */
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

/** File-attach dispatch by MIME type (PDF/audio/CSV) - preserved from the legacy app.
 *
 * specs/039-composer-interaction-states-redesign — the footer's action set is now
 * state-dependent (contracts/composer-voice-states.md) rather than always-mounted: exactly
 * one of {attach + mic + continuous-conversation entry} (empty), {send} (typing, in either
 * voice mode), {cancel + confirm} (click-to-talk review), {non-interactive mic-fill
 * indicator} (hold-to-talk), or {mute + exit} (Continuous idle-listening) renders at a time.
 * Speaker-mute (Lucy's own voice output) is a separate, unrelated control that lives in
 * `ExpandedChatPanel`'s header, not here. */
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

  // specs/039-composer-interaction-states-redesign T014 (analysis remediation E1, corrected
  // per round-2 finding F4) — spec.md Edge Case: a hold-to-talk recording MUST NOT be able to
  // remain open indefinitely if the tab loses focus or the screen locks. Calls
  // `onStopCapture()` directly rather than routing through `resolveGestureOnRelease()`, since
  // that function re-derives tap-vs-hold from elapsed time and would leave a still-
  // tap-classified press waiting in `isAwaitingTapReview` for a Finish/Cancel click the user
  // cannot reach with the tab hidden — the tab/window losing visibility makes the tap/hold
  // distinction moot either way, so this safeguard stops capture unconditionally.
  useEffect(() => {
    const forceStopIfCapturing = () => {
      if (!isCapturingRef.current) return
      isCapturingRef.current = false
      setIsAwaitingTapReview(false)
      onStopCapture()
    }
    const handleVisibilityChange = () => {
      if (document.hidden) forceStopIfCapturing()
    }
    document.addEventListener('visibilitychange', handleVisibilityChange)
    window.addEventListener('blur', forceStopIfCapturing)
    return () => {
      document.removeEventListener('visibilitychange', handleVisibilityChange)
      window.removeEventListener('blur', forceStopIfCapturing)
    }
  }, [onStopCapture])

  // Continuous mode's mute action is a plain listening on/off toggle — no hold-vs-tap gesture
  // is needed since there's no separate "review before sending" step to distinguish (FR-013).
  // Distinct from Lucy's own speaker-mute in ExpandedChatPanel's header — this toggles the
  // user's own microphone input, not the assistant's voice output.
  const handleToggleContinuousMute = () => {
    if (isListening) {
      onStopCapture()
    } else {
      onStartCapture()
    }
  }

  const isRecordingActive = Boolean(recording) && recording?.phase !== 'idle'

  // specs/039-composer-interaction-states-redesign FR-001/FR-002/FR-012 — the composer's
  // visible action set is derived from this single state, not several independent booleans
  // (data-model.md "Composer State"). Recording takes priority over everything (including
  // typed text — starting a recording is only reachable from the empty state to begin with,
  // per FR-005/FR-008, so a recording can never coexist with `value !== ''`); typed text takes
  // priority over Continuous mode's idle-listening view (Continuous *typing* shows the same
  // send-only footer as ordinary typing — the two only differ in what they return to once
  // `value` empties again, which this component doesn't need to know since that's driven
  // entirely by the `conversationMode`/`isListening` props it already re-reads). Gating the
  // Continuous branch on `conversationMode === 'Continuous'` alone (not additionally on
  // `isListening`) is deliberate: muting must only change the mute button's own icon
  // (FR-013), not make the whole footer revert to the empty view and lose the exit action.
  const composerVisualState: 'recording' | 'typing' | 'continuous' | 'empty' = isRecordingActive
    ? 'recording'
    : value !== ''
      ? 'typing'
      : conversationMode === 'Continuous'
        ? 'continuous'
        : 'empty'

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
          {/* specs/039-composer-interaction-states-redesign — the mic button spans both the
              'empty' and 'recording' visual states as a SINGLE persistent element (never
              unmounted/remounted between them): the specs/033 pointer-capture fix depends on
              `pointerup` landing on the exact same DOM node `pointerdown` fired on, and
              splitting it across two different conditional branches (as an earlier revision
              of this file did) silently breaks that by letting React replace the element the
              moment `recording.phase` changes. Only `isAwaitingTapReview` swaps it out (for
              `RecordingReviewControls`) — that's fine, since by then the user has already
              released and the gesture is over. */}
          {(composerVisualState === 'empty' || composerVisualState === 'recording') && (
            <>
              {composerVisualState === 'empty' && (
                <Tooltip title="Attach file">
                  <IconButton
                    onClick={() => fileInputRef.current?.click()}
                    aria-label="Attach file"
                  >
                    <RiAttachment2 />
                  </IconButton>
                </Tooltip>
              )}

              {/* specs/040-composer-interaction-bug-fixes US1 — pins the attachment button to
                  the leading edge and the mic/continuous-entry group to the trailing edge
                  (Figure 1), instead of both clustering at the left with the row's other
                  spacer (below) having nothing left to push against. Only present for 'empty':
                  'recording' has no attachment button to separate from anything. */}
              {composerVisualState === 'empty' && <Box sx={{ flex: 1 }} />}

              {/* FR-005/FR-019: live waveform alongside the mic while actively recording —
                  nothing left to visualize once capture has stopped and transcription has
                  begun. Shown for both the tap and hold branches, since they're physically
                  identical until release resolves which one it becomes. */}
              {recording?.phase === 'recording' && (
                <Box sx={{ width: 64 }}>
                  <VoiceAnalyzer state="listening" getIntensity={recording.getIntensity} />
                </Box>
              )}

              {isAwaitingTapReview && recording ? (
                <RecordingReviewControls
                  phase={recording.phase}
                  onFinish={handleTapReviewFinish}
                  onCancelRecording={handleTapReviewCancel}
                  placement="right"
                />
              ) : (
                // Only ever reached in Push-to-Talk mode — Continuous mode's idle-listening
                // state renders the 'continuous' branch below instead. Icon reflects whether a
                // recording is currently active (RiMicFill, FR-009/Figure 9) or idle
                // (RiMicLine/RiMicOffLine) — the aria-label/tooltip convention itself
                // (isListening-based) is unchanged from before this feature.
                <Tooltip title={isListening ? 'Stop voice input' : 'Start voice input'}>
                  <span>
                    <IconButton
                      onPointerDown={handleMicPointerDown}
                      onPointerUp={handleMicPointerUp}
                      onPointerLeave={handleMicPointerUp}
                      onPointerCancel={handleMicPointerUp}
                      onKeyDown={handleMicKeyDown}
                      onKeyUp={handleMicKeyUp}
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
                      {isRecordingActive ? (
                        <RiMicFill />
                      ) : isListening ? (
                        <RiMicOffLine />
                      ) : (
                        <RiMicLine />
                      )}
                    </IconButton>
                  </span>
                </Tooltip>
              )}

              {composerVisualState === 'empty' && (
                <>
                  {/* specs/039-composer-interaction-states-redesign FR-012, Clarifications
                      (one-click hybrid) — reuses the same onToggleMode the exit action (below,
                      Continuous branch) calls; the caller pairs it with onStartCapture and the
                      persisted-preference save. */}
                  <Tooltip title="Start continuous conversation">
                    <IconButton
                      onClick={onToggleMode}
                      aria-label="Start continuous conversation"
                      size="small"
                    >
                      <RiVoiceprintLine fontSize="small" />
                    </IconButton>
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
            </>
          )}

          {composerVisualState === 'continuous' && (
            <>
              <Tooltip title={isListening ? 'Mute microphone' : 'Unmute microphone'}>
                <IconButton
                  onClick={handleToggleContinuousMute}
                  aria-label={isListening ? 'Mute microphone' : 'Unmute microphone'}
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
              <Tooltip title="Exit continuous conversation">
                <IconButton onClick={onToggleMode} aria-label="Exit continuous conversation">
                  <RiStopLine />
                </IconButton>
              </Tooltip>
            </>
          )}

          {composerVisualState !== 'recording' &&
            voicePreferencesUnavailable &&
            composerVisualState !== 'empty' && (
              <Tooltip title="Using default voice settings — couldn't load your saved preferences">
                <RiErrorWarningLine
                  aria-label="Voice preferences unavailable, using defaults"
                  role="img"
                  size={16}
                  style={{ opacity: 0.6, flexShrink: 0 }}
                />
              </Tooltip>
            )}

          {/* specs/040-composer-interaction-bug-fixes US1 — narrowed from unconditional: this
              spacer's only remaining job is pushing Send to the trailing edge while typing.
              'empty' now has its own dedicated spacer (above); 'recording'/'continuous' render
              nothing after this point today, so an unconditional trailing spacer here had no
              visible effect for them anyway. */}
          {composerVisualState === 'typing' && <Box sx={{ flex: 1 }} />}

          {composerVisualState === 'typing' && (
            <Tooltip title="Send message">
              {/* MUI Tooltip cannot attach directly to a disabled element — same
                  <span> wrapper pattern already used above for the recording indicator. */}
              <span>
                <IconButton
                  onClick={onSend}
                  disabled={disabled || !value.trim()}
                  aria-label="Send message"
                  sx={{
                    bgcolor: value.trim() && !disabled ? 'primary.main' : 'transparent',
                    color: value.trim() && !disabled ? 'primary.contrastText' : 'text.disabled',
                    '&:hover': {
                      bgcolor: value.trim() && !disabled ? 'primary.dark' : 'action.hover',
                    },
                    transition: (theme) => theme.transitions.create(['background-color', 'color']),
                  }}
                >
                  <RiSendPlane2Fill size={20} />
                </IconButton>
              </span>
            </Tooltip>
          )}
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
