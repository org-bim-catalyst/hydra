import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'
import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError } from '../../../api/httpClient'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'
import { resetSiteBoundary } from '../../chat/api/chatsApi'
import { useOutlineResetStore } from '../../../viewer/siteBoundaryEdit/outlineResetStore'
import { applyBoundaryToViewer } from '../../../viewer/siteBoundaryEdit/useSiteBoundaryEditMode'

/**
 * specs/079 (US5, FR-025 to FR-028): confirms, then gives up the hand edits so the outline Lucy found
 * is drawn again. A failure (the outline changed meanwhile, the server was unreachable) stays in the
 * dialog, in words, with the option to try again.
 */
export function SiteBoundaryResetDialog() {
  const open = useOutlineResetStore((s) => s.open)
  const hide = useOutlineResetStore((s) => s.hide)
  const siteName = useActiveSiteBoundaryStore((s) => s.siteName)
  const queryClient = useQueryClient()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const close = () => {
    setError(null)
    hide()
  }

  const reset = async () => {
    const { chatId, revision } = useActiveSiteBoundaryStore.getState()
    if (!chatId || !revision) {
      setError("This outline can't be reset right now - reload the chat and try again.")
      return
    }

    setBusy(true)
    setError(null)
    try {
      const result = await resetSiteBoundary(chatId, { expectedRevision: revision })
      applyBoundaryToViewer(chatId, result.activeBoundary)
      void queryClient.invalidateQueries({ queryKey: ['chats', chatId, 'messages'] })
      void queryClient.invalidateQueries({ queryKey: ['chats', chatId, 'detail'] })
      hide()
    } catch (caught) {
      setError(
        caught instanceof ApiError && caught.status === 409
          ? 'The outline changed since you last looked at it. Reload the chat, then try again.'
          : caught instanceof ApiError
            ? (caught.detail ?? caught.message)
            : caught instanceof Error
              ? caught.message
              : 'The outline could not be reset.',
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onClose={busy ? undefined : close} maxWidth="xs" fullWidth>
      <DialogTitle>Reset to Lucy&apos;s outline?</DialogTitle>
      <DialogContent>
        <DialogContentText>
          {siteName ? `Your edits to the outline of ${siteName} ` : 'Your edits to this outline '}
          will be discarded and the outline Lucy found is drawn again. Other chats about this site go back to it too.
        </DialogContentText>
        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {error}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={close} disabled={busy}>
          Keep my edits
        </Button>
        <Button onClick={() => void reset()} disabled={busy} color="error" variant="contained">
          Reset
        </Button>
      </DialogActions>
    </Dialog>
  )
}
