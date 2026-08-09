import { Alert, Button, CircularProgress, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Stack, Typography } from '@mui/material'
import { useState } from 'react'
import { useMemory } from '../hooks/useMemories'
import { useResolveMemoryConflict } from '../hooks/useMemoryMutations'

interface MemoryConflictDialogProps {
  open: boolean
  memoryId: string | null
  onClose: () => void
}

/**
 * spec.md FR-016, User Story 6 AC2/AC3 (clarified 2026-08-09) — resolves an ambiguous conflict
 * asynchronously via the Memory Center, independent of whatever live conversation originally
 * surfaced it. `memoryId` is either side of the conflict (contracts/memories-api.md); the dialog
 * shows that memory's own current content plus the three resolution choices.
 */
export function MemoryConflictDialog({ open, memoryId, onClose }: MemoryConflictDialogProps) {
  const { data: memory, isLoading } = useMemory(memoryId)
  const resolveConflict = useResolveMemoryConflict()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const resolve = (resolution: 'KeepExisting' | 'KeepNew' | 'KeepBoth') => {
    if (!memoryId) return
    resolveConflict.mutate(
      { id: memoryId, resolution },
      {
        onSuccess: () => {
          setErrorMessage(null)
          onClose()
        },
        onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not resolve the conflict. Please try again.'),
      },
    )
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth aria-labelledby="memory-conflict-dialog-title">
      <DialogTitle id="memory-conflict-dialog-title">Lucy noticed a possible conflict</DialogTitle>
      <DialogContent>
        {isLoading && <CircularProgress size={24} />}
        {!isLoading && memory && (
          <>
            <DialogContentText sx={{ mb: 2 }}>
              This might contradict or overlap with something Lucy already remembers about you:
            </DialogContentText>
            <Typography variant="body1" sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 1 }}>
              {memory.content}
            </Typography>
          </>
        )}
        {errorMessage && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {errorMessage}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Stack direction="row" spacing={1} sx={{ p: 1, flexWrap: 'wrap' }}>
          <Button onClick={() => resolve('KeepExisting')} disabled={resolveConflict.isPending}>
            Keep the older one
          </Button>
          <Button onClick={() => resolve('KeepNew')} disabled={resolveConflict.isPending}>
            Keep the newer one
          </Button>
          <Button variant="contained" onClick={() => resolve('KeepBoth')} disabled={resolveConflict.isPending}>
            Keep both
          </Button>
        </Stack>
      </DialogActions>
    </Dialog>
  )
}
