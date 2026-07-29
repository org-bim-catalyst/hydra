import MicIcon from '@mui/icons-material/Mic'
import MicOffIcon from '@mui/icons-material/MicOff'
import SendIcon from '@mui/icons-material/Send'
import AttachFileIcon from '@mui/icons-material/AttachFile'
import { Alert, Box, IconButton, Paper, Snackbar, Stack, TextField } from '@mui/material'
import { useRef, useState } from 'react'
import { transcribeAudio } from '../api/aiApi'
import { usePdfTextExtraction } from '../pdf/usePdfTextExtraction'
import { useVoiceRecognition } from '../voice/useVoiceRecognition'

interface ChatComposerProps {
  onSend: (text: string) => void
  disabled?: boolean
  language: string
}

/** File-attach dispatch by MIME type (PDF/audio/CSV) and voice input — preserved from the legacy app. */
export function ChatComposer({ onSend, disabled, language }: ChatComposerProps) {
  const [text, setText] = useState('')
  const fileInputRef = useRef<HTMLInputElement>(null)
  const { extractText } = usePdfTextExtraction()
  const voice = useVoiceRecognition(language)

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

  const toggleVoice = () => {
    if (voice.isListening) {
      voice.stop()
    } else {
      voice.start((transcript) => setText((prev) => `${prev} ${transcript}`.trim()))
    }
  }

  return (
    <Box sx={{ p: 2, pt: 0 }}>
      <Paper
        variant="outlined"
        sx={{ maxWidth: 800, mx: 'auto', borderRadius: 4, px: 1, py: 0.5 }}
      >
        <Stack direction="row" spacing={0.5} sx={{ alignItems: 'flex-end' }}>
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
          <IconButton onClick={() => fileInputRef.current?.click()} aria-label="Attach file" sx={{ mb: 0.5 }}>
            <AttachFileIcon />
          </IconButton>
          {voice.isSupported && (
            <IconButton
              onClick={toggleVoice}
              aria-label="Voice input"
              color={voice.isListening ? 'error' : 'default'}
              sx={{ mb: 0.5 }}
            >
              {voice.isListening ? <MicOffIcon /> : <MicIcon />}
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
            sx={{ mb: 0.5 }}
          >
            <SendIcon />
          </IconButton>
        </Stack>
      </Paper>
      <Snackbar open={Boolean(voice.error)} autoHideDuration={5000} onClose={() => voice.clearError()}>
        <Alert severity="error" variant="filled">
          {voice.error}
        </Alert>
      </Snackbar>
    </Box>
  )
}
