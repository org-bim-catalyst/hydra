import { useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControlLabel,
  List,
  ListItem,
  ListItemText,
  Radio,
  RadioGroup,
  Typography,
} from '@mui/material'

export type BulkActionScope = 'page' | 'all'

export interface BulkActionSkip {
  id: string
  reason: string
}

export interface BulkActionOutcome {
  succeededCount: number
  skipped: BulkActionSkip[]
}

interface BulkActionConfirmDialogProps {
  open: boolean
  onClose: () => void
  /** Verb describing the action, e.g. "Lock", "Delete". */
  actionLabel: string
  /** Rows selected on the current page. */
  pageSelectedCount: number
  /** Total rows matching the active filter across every page, once resolved. `undefined` while resolving. */
  allMatchingCount?: number
  onConfirm: (scope: BulkActionScope) => Promise<BulkActionOutcome>
}

/** Shared confirmation + per-row result summary for every bulk action (FR-007/FR-009). */
export function BulkActionConfirmDialog({
  open,
  onClose,
  actionLabel,
  pageSelectedCount,
  allMatchingCount,
  onConfirm,
}: BulkActionConfirmDialogProps) {
  const [scope, setScope] = useState<BulkActionScope>('page')
  const [isRunning, setIsRunning] = useState(false)
  const [outcome, setOutcome] = useState<BulkActionOutcome | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  function handleClose() {
    setScope('page')
    setOutcome(null)
    setErrorMessage(null)
    onClose()
  }

  async function handleConfirm() {
    setIsRunning(true)
    setErrorMessage(null)
    try {
      const result = await onConfirm(scope)
      setOutcome(result)
    } catch {
      setErrorMessage('Something went wrong. Please try again.')
    } finally {
      setIsRunning(false)
    }
  }

  return (
    <Dialog open={open} onClose={handleClose} fullWidth maxWidth="sm">
      <DialogTitle>{outcome ? `${actionLabel} complete` : `${actionLabel} selected items?`}</DialogTitle>
      <DialogContent>
        {outcome ? (
          <>
            <DialogContentText sx={{ mb: 1 }}>
              {outcome.succeededCount} item{outcome.succeededCount === 1 ? '' : 's'} succeeded.
            </DialogContentText>
            {outcome.skipped.length > 0 && (
              <>
                <Typography variant="subtitle2">{outcome.skipped.length} skipped:</Typography>
                <List dense>
                  {outcome.skipped.map((skip) => (
                    <ListItem key={skip.id} disableGutters>
                      <ListItemText primary={skip.id} secondary={skip.reason} />
                    </ListItem>
                  ))}
                </List>
              </>
            )}
          </>
        ) : (
          <>
            {errorMessage && (
              <Alert severity="error" sx={{ mb: 2 }}>
                {errorMessage}
              </Alert>
            )}
            <RadioGroup value={scope} onChange={(e) => setScope(e.target.value as BulkActionScope)}>
              <FormControlLabel value="page" control={<Radio />} label={`${pageSelectedCount} selected on this page`} />
              <FormControlLabel
                value="all"
                control={<Radio />}
                disabled={allMatchingCount === undefined}
                label={
                  allMatchingCount === undefined
                    ? 'Resolving total matching…'
                    : `All ${allMatchingCount} matching items`
                }
              />
            </RadioGroup>
          </>
        )}
      </DialogContent>
      <DialogActions>
        {outcome ? (
          <Button onClick={handleClose} autoFocus>
            Close
          </Button>
        ) : (
          <>
            <Button onClick={handleClose} disabled={isRunning}>
              Cancel
            </Button>
            <Button onClick={handleConfirm} color="error" variant="contained" disabled={isRunning} autoFocus>
              {actionLabel}
            </Button>
          </>
        )}
      </DialogActions>
    </Dialog>
  )
}
