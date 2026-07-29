import CheckIcon from '@mui/icons-material/Check'
import CloseIcon from '@mui/icons-material/Close'
import MicIcon from '@mui/icons-material/Mic'
import SendIcon from '@mui/icons-material/Send'
import AttachFileIcon from '@mui/icons-material/AttachFile'
import { Alert, Box, CircularProgress, IconButton, Paper, Snackbar, Stack, TextField, useTheme } from '@mui/material'
import { useRef, useState } from 'react'
import { transcribeAudio, transcribeMicrophoneAudio } from '../api/aiApi'
import { usePdfTextExtraction } from '../pdf/usePdfTextExtraction'
import { useWavRecorder } from '../voice/useWavRecorder'
import { VoiceWaveform } from './VoiceWaveform'

interface ChatComposerProps {
  onSend: (text: string) => void
  disabled?: boolean
}

/** File-attach dispatch by MIME type (PDF/audio/CSV) and voice input — preserved from the legacy app. */
export function ChatComposer({ onSend, disabled }: ChatComposerProps) {
  const [text, setText] = useState('')
  const [isTranscribing, setIsTranscribing] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const { extractText } = usePdfTextExtraction()
  const voice = useWavRecorder()
  const theme = useTheme()

  const handleSend = () => {
    if (!text.trim()) return
    onSend(text.trim())
    setText('')
  }

  const handleFile = async (file: File) => {
    if (file.type === 'application/pdf') {
      const extracted = await extractText(file)
      setText((prev) => `${prev}${extracted}`)
    } else if (file.type.startsWith('audio/')) {
      const transcript = await transcribeAudio(file)
      setText((prev) => `${prev}${transcript}`)
    } else if (file.type === 'text/csv' || file.name.endsWith('.csv')) {
      const csvText = await file.text()
      setText((prev) => `${prev}${csvText}`)
    }
  }

  const handleCancelVoice = () => {
    voice.discard()
  }

  const handleConfirmVoice = async () => {
    const wavBlob = voice.stop()
    if (!wavBlob) return

    setIsTranscribing(true)
    try {
      const transcript = await transcribeMicrophoneAudio(wavBlob)
      if (transcript.trim()) {
        setText('')
        onSend(transcript.trim())
      }
    } catch {
      voice.setError('Voice input failed. Please try again.')
    } finally {
      setIsTranscribing(false)
    }
  }

  return (
    <Box sx={{ p: 2, pt: 0 }}>
      <Paper
        variant="outlined"
        sx={{
          maxWidth: 800,
          mx: 'auto',
          borderRadius: '999px',
          px: 1,
          minHeight: 56,
          display: 'flex',
          alignItems: 'center',
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
        {voice.isRecording ? (
          <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', width: '100%' }}>
            <IconButton onClick={handleCancelVoice} aria-label="Cancel voice input">
              <CloseIcon />
            </IconButton>
            <Box sx={{ flex: 1, px: 1 }}>
              <VoiceWaveform getLevels={voice.getLevels} color={theme.palette.primary.main} />
            </Box>
            <IconButton onClick={() => void handleConfirmVoice()} aria-label="Finish and send voice input" color="primary">
              <CheckIcon />
            </IconButton>
          </Stack>
        ) : isTranscribing ? (
          <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center', width: '100%', px: 1.5 }}>
            <CircularProgress size={20} />
            <Box sx={{ color: 'text.secondary', fontSize: '0.875rem' }}>Transcribing...</Box>
          </Stack>
        ) : (
          <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center', width: '100%' }}>
            <IconButton onClick={() => fileInputRef.current?.click()} aria-label="Attach file">
              <AttachFileIcon />
            </IconButton>
            {voice.isSupported && (
              <IconButton onClick={() => void voice.start()} aria-label="Voice input">
                <MicIcon />
              </IconButton>
            )}
            <TextField
              fullWidth
              multiline
              maxRows={6}
              variant="standard"
              placeholder="Message Ask Lucy..."
              value={text}
              onChange={(e) => setText(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault()
                  handleSend()
                }
              }}
              disabled={disabled}
              slotProps={{ input: { disableUnderline: true } }}
              sx={{ py: 1.25 }}
            />
            <IconButton
              color="primary"
              onClick={handleSend}
              disabled={disabled || !text.trim()}
              aria-label="Send message"
            >
              <SendIcon />
            </IconButton>
          </Stack>
        )}
      </Paper>
      <Snackbar open={Boolean(voice.error)} autoHideDuration={5000} onClose={() => voice.clearError()}>
        <Alert severity="error" variant="filled">
          {voice.error}
        </Alert>
      </Snackbar>
    </Box>
  )
}
