import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  Typography,
} from '@mui/material'
import { useFormat, useT } from '../../../i18n/useT'
import type { BulkActionOutcome } from '../bulkRunner'

export type { BulkActionOutcome, BulkActionSkip } from '../bulkRunner'

type Step = 'confirm' | 'running' | 'done'

interface BulkActionConfirmDialogProps {
  open: boolean
  onClose: () => void
  /** Verb describing the action, e.g. "Lock", "Delete". */
  actionLabel: string
  /** Present-progressive form for the progress step, e.g. "Deleting", "Locking". Defaults to `${actionLabel}ing`. */
  progressVerb?: string
  /** Already-resolved count of items this action will target (FR-009: the admin sees the real number before confirming). */
  itemCount: number
  /** Runs the action, reporting `(done, total)` after each batch so the dialog can show live progress. */
  onConfirm: (onProgress: (done: number, total: number) => void) => Promise<BulkActionOutcome>
}

/** Shared confirm → progress → result flow for every bulk action (FR-007/FR-009). */
export function BulkActionConfirmDialog({
  open,
  onClose,
  actionLabel,
  progressVerb,
  itemCount,
  onConfirm,
}: BulkActionConfirmDialogProps) {
  const [step, setStep] = useState<Step>('confirm')
  const [progress, setProgress] = useState({ done: 0, total: itemCount })
  const [outcome, setOutcome] = useState<BulkActionOutcome | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const t = useT('admin.users')
  const { language } = useFormat()
  // English reads "Do you want to delete 3 items?"; the caller's label is already localized for other languages.
  const action = language === 'en' ? actionLabel.toLowerCase() : actionLabel

  function reset() {
    setStep('confirm')
    setProgress({ done: 0, total: itemCount })
    setOutcome(null)
    setErrorMessage(null)
  }

  function handleClose() {
    reset()
    onClose()
  }

  async function handleConfirm() {
    setStep('running')
    setErrorMessage(null)
    try {
      const result = await onConfirm((done, total) => setProgress({ done, total }))
      setOutcome(result)
      setStep('done')
    } catch {
      setErrorMessage(t('bulk.failed'))
      setStep('confirm')
    }
  }

  return (
    <Dialog
      open={open}
      onClose={step === 'running' ? undefined : handleClose}
      fullWidth
      maxWidth="sm"
    >
      {step === 'confirm' && (
        <>
          <DialogTitle>{t('bulk.confirmTitle', { action: actionLabel })}</DialogTitle>
          <DialogContent>
            {errorMessage && (
              <Alert severity="error" sx={{ mb: 2 }}>
                {errorMessage}
              </Alert>
            )}
            <DialogContentText>
              {t('bulk.confirmBody', { action, count: itemCount })}
            </DialogContentText>
          </DialogContent>
          <DialogActions>
            <Button onClick={handleClose}>{t('bulk.cancel')}</Button>
            <Button onClick={handleConfirm} color="error" variant="contained" autoFocus>
              {actionLabel}
            </Button>
          </DialogActions>
        </>
      )}

      {step === 'running' && (
        <DialogContent>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, py: 3 }}>
            <CircularProgress size={24} />
            <Typography>
              {t('bulk.running', {
                verb:
                  progressVerb ??
                  (language === 'en'
                    ? `${actionLabel}ing`
                    : t('bulk.progress.fallback', { action: actionLabel })),
                done: progress.done,
                total: progress.total,
              })}
            </Typography>
          </Box>
        </DialogContent>
      )}

      {step === 'done' && outcome && (
        <>
          <DialogTitle>{t('bulk.completeTitle', { action: actionLabel })}</DialogTitle>
          <DialogContent>
            <DialogContentText sx={{ mb: 1 }}>
              {t('bulk.succeeded', { count: outcome.succeededCount })}
            </DialogContentText>
            {outcome.skipped.length > 0 && (
              <>
                <Typography variant="subtitle2">
                  {t('bulk.skipped', { count: outcome.skipped.length })}
                </Typography>
                <List dense>
                  {outcome.skipped.map((skip) => (
                    <ListItem key={skip.id} disableGutters>
                      <ListItemText
                        primary={<bdi dir="ltr">{skip.id}</bdi>}
                        secondary={skip.reason}
                      />
                    </ListItem>
                  ))}
                </List>
              </>
            )}
          </DialogContent>
          <DialogActions>
            <Button onClick={handleClose} autoFocus>
              {t('bulk.done')}
            </Button>
          </DialogActions>
        </>
      )}
    </Dialog>
  )
}
