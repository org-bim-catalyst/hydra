import { Alert, Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'
import { useState } from 'react'
import { useChatSwitchGuardStore } from '../../../viewer/siteBoundaryEdit/chatSwitchGuard'
import { hasSiteBoundaryEditRuntime, siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'

interface Props {
  /** On a page with no map (Settings) the outline can't be saved from here; this takes the user back to where it can. */
  onReturnToEditor?: () => void
}

/**
 * specs/079 (FR-029): asks what to do with unsaved outline changes before another chat opens. Save keeps the
 * edit (and then switches); Discard drops it; Stay cancels the switch. Where the map isn't showing, Save is
 * replaced by a way back to the editor, since the outline can only be saved from there.
 */
export function SiteBoundaryChatSwitchDialog({ onReturnToEditor }: Props) {
  const pending = useChatSwitchGuardStore((s) => s.pending)
  const release = useChatSwitchGuardStore((s) => s.release)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const canSave = hasSiteBoundaryEditRuntime()

  const stay = () => {
    setError(null)
    release()
  }

  const leave = () => {
    const proceed = pending?.proceed
    release()
    proceed?.()
  }

  const save = async () => {
    setBusy(true)
    setError(null)
    try {
      await siteBoundaryEditActions.done()
      // A failed save leaves the session open with its own message; the switch waits.
      const session = useSiteBoundaryEditStore.getState().session
      if (session === null) leave()
      else setError('The outline could not be saved - see the message on the map.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'The outline could not be saved.')
    } finally {
      setBusy(false)
    }
  }

  const discard = () => {
    if (canSave) siteBoundaryEditActions.cancel()
    else useSiteBoundaryEditStore.getState().end()
    leave()
  }

  return (
    <Dialog open={pending !== null} onClose={busy ? undefined : stay} maxWidth="xs" fullWidth>
      <DialogTitle>Save your outline changes?</DialogTitle>
      <DialogContent>
        <DialogContentText>You changed the site outline and haven&apos;t saved it. Opening another chat would lose those changes.</DialogContentText>
        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {error}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={stay} disabled={busy}>
          Stay
        </Button>
        <Button onClick={discard} disabled={busy} color="error">
          Discard
        </Button>
        {canSave ? (
          <Button onClick={() => void save()} disabled={busy} variant="contained">
            Save
          </Button>
        ) : (
          <Button
            onClick={() => {
              stay()
              onReturnToEditor?.()
            }}
            variant="contained"
          >
            Go back and save
          </Button>
        )}
      </DialogActions>
    </Dialog>
  )
}
