import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle } from '@mui/material'

interface ConfirmDialogProps {
  open: boolean
  title: string
  description: string
  confirmLabel?: string
  destructive?: boolean
  onConfirm: () => void
  onCancel: () => void
}

/**
 * Shared confirmation gate for destructive, irreversible-feeling actions — Clear Messages
 * (FR-011) and Permanent Delete (FR-004/FR-005) both use this (constitution §7: a new shared
 * component needs at least two features, satisfied here). The action only proceeds when the
 * user explicitly confirms; the corresponding Application command re-enforces the same rule
 * server-side (constitution §2.VIII — no silent failures, no client-only gate).
 */
export function ConfirmDialog({ open, title, description, confirmLabel = 'Confirm', destructive = true, onConfirm, onCancel }: ConfirmDialogProps) {
  return (
    <Dialog open={open} onClose={onCancel} aria-labelledby="confirm-dialog-title">
      <DialogTitle id="confirm-dialog-title">{title}</DialogTitle>
      <DialogContent>
        <DialogContentText>{description}</DialogContentText>
      </DialogContent>
      <DialogActions>
        <Button onClick={onCancel}>Cancel</Button>
        <Button onClick={onConfirm} color={destructive ? 'error' : 'primary'} variant="contained" autoFocus>
          {confirmLabel}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
