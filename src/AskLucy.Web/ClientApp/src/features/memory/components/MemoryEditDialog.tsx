import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material'
import { useForm } from 'react-hook-form'
import type { MemoryListItem } from '../api/memoryApi'

interface MemoryEditFormValues {
  content: string
}

interface MemoryEditDialogProps {
  open: boolean
  memory: MemoryListItem | undefined
  submitting: boolean
  errorMessage: string | null
  onSubmit: (content: string) => void
  onClose: () => void
}

/** spec.md FR-019, User Story 2 AC2 — the parent remounts this component (via a `key` on the memory id) each time a different memory is opened, so `content` only needs a plain `useForm` initializer. */
export function MemoryEditDialog({ open, memory, submitting, errorMessage, onSubmit, onClose }: MemoryEditDialogProps) {
  const { register, handleSubmit, formState } = useForm<MemoryEditFormValues>({
    values: { content: memory?.content ?? '' },
  })

  const submit = handleSubmit((values) => onSubmit(values.content))

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth aria-labelledby="memory-edit-dialog-title">
      <DialogTitle id="memory-edit-dialog-title">Edit memory</DialogTitle>
      <Box component="form" onSubmit={submit}>
        <DialogContent>
          <TextField
            label="Content"
            fullWidth
            multiline
            rows={4}
            autoFocus
            required
            {...register('content', { required: 'Memory content is required.' })}
            error={Boolean(formState.errors.content)}
            helperText={formState.errors.content?.message}
          />
          {errorMessage && (
            <Alert severity="error" sx={{ mt: 2 }}>
              {errorMessage}
            </Alert>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={submitting}>
            Save
          </Button>
        </DialogActions>
      </Box>
    </Dialog>
  )
}
