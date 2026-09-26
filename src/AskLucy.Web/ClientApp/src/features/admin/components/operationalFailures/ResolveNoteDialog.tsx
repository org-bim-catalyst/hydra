import { useState } from 'react'
import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material'

export const MAX_RESOLUTION_NOTE_LENGTH = 500

/** specs/074 FR-024 — the optional note a resolve records (≤ 500 characters, counted as you type). */
export function ResolveNoteDialog({
  open,
  title,
  confirmLabel,
  pending,
  errorMessage,
  onCancel,
  onConfirm,
}: {
  open: boolean
  title: string
  confirmLabel: string
  pending: boolean
  errorMessage: string | null
  onCancel: () => void
  onConfirm: (note: string | null) => void
}) {
  const [note, setNote] = useState('')

  return (
    <Dialog
      open={open}
      onClose={pending ? undefined : onCancel}
      fullWidth
      maxWidth="sm"
      slotProps={{ transition: { onExited: () => setNote('') } }}
    >
      <DialogTitle>{title}</DialogTitle>
      <DialogContent>
        {errorMessage && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {errorMessage}
          </Alert>
        )}
        <TextField
          autoFocus
          fullWidth
          multiline
          minRows={3}
          label="Note (optional)"
          value={note}
          onChange={(event) => setNote(event.target.value)}
          slotProps={{ htmlInput: { maxLength: MAX_RESOLUTION_NOTE_LENGTH } }}
          helperText={`${note.length}/${MAX_RESOLUTION_NOTE_LENGTH}`}
          sx={{ mt: 1 }}
        />
      </DialogContent>
      <DialogActions>
        <Button onClick={onCancel} disabled={pending}>
          Cancel
        </Button>
        <Button variant="contained" onClick={() => onConfirm(note.trim() || null)} disabled={pending}>
          {confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
