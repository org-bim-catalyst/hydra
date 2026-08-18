import CloseIcon from '@mui/icons-material/Close'
import MicIcon from '@mui/icons-material/Mic'
import MicOffIcon from '@mui/icons-material/MicOff'
import SendIcon from '@mui/icons-material/Send'
import AttachFileIcon from '@mui/icons-material/AttachFile'
import {
  Alert,
  Box,
  IconButton,
  Paper,
  Snackbar,
  Stack,
  TextField,
} from '@mui/material'
import { useRef } from 'react'
import { transcribeAudio } from '../api/aiApi'
import { usePdfTextExtraction } from '../pdf/usePdfTextExtraction'
import type { MicrophonePermissionState } from '../voice/useSpeechRecognition'
import { radius } from '../../../theme'
import { usePrefersReducedMotion } from '../../../hooks/usePrefersReducedMotion'

export interface ChatComposerProps {
  value: string
  onChange: (text: string) => void
  onSend: () => void
  disabled?: boolean
  /** SPEC-013 US2: which voice input mode is active - governs whether the mic control is a
   * manual hold/toggle trigger (Push-to-Talk) or purely a status display (Continuous, where
   * capture is already always-on - research.md Decision 4). */
  conversationMode: 'PushToTalk' | 'Continuous'
  isListening: boolean
  permissionState: MicrophonePermissionState
  captureError: string | null
  onStartCapture: () => void
  onStopCapture: () => void
  onCancelCapture: () => void
  onClearCaptureError: () => void
}

const HOLD_THRESHOLD_MS = 350

/** File-attach dispatch by MIME type (PDF/audio/CSV) - preserved from the legacy app. Voice
 * input is handled by the mic control below, driven by the `useSpeechRecognition` instance
 * `ConversationView` owns (SPEC-013 US2 - replaces the former one-shot
 * `useWavRecorder`/`transcribeMicrophoneAudio` dictate flow). */
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
  onCancelCapture,
  onClearCaptureError,
}: ChatComposerProps) {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const { extractText } = usePdfTextExtraction()
  const prefersReducedMotion = usePrefersReducedMotion()

  // Distinguishes a genuine *hold* (press, speak, release) from a *quick tap* meant to toggle
  // listening on and leave it on (Clarification Q1: both are supported on the same control).
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

  const showMicButton = conversationMode === 'PushToTalk'

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
            <AttachFileIcon />
          </IconButton>

          {showMicButton && (
            <IconButton
              onPointerDown={handleMicPointerDown}
              onPointerUp={handleMicPointerUp}
              onPointerLeave={handleMicPointerUp}
              onClick={handleMicClick}
              onKeyDown={handleMicKeyDown}
              onKeyUp={handleMicKeyUp}
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
              {isListening ? <MicOffIcon /> : <MicIcon />}
            </IconButton>
          )}

          {isListening && (
            <>
              <IconButton onClick={onCancelCapture} aria-label="Cancel voice input" size="small">
                <CloseIcon fontSize="small" />
              </IconButton>
              <Box sx={{ color: 'secondary.main', fontSize: '0.875rem', fontWeight: 500 }}>Listening…</Box>
            </>
          )}

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
            <SendIcon fontSize="small" />
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
      <Snackbar
        open={Boolean(captureError)}
        autoHideDuration={5000}
        onClose={onClearCaptureError}
      >
        <Alert severity="error" variant="filled" onClose={onClearCaptureError}>
          {captureError}
        </Alert>
      </Snackbar>
    </Box>
  )
}
