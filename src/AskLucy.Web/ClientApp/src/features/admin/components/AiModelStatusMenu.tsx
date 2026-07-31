import { useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  IconButton,
  Menu,
  MenuItem,
  Snackbar,
} from '@mui/material'
import MoreVertIcon from '@mui/icons-material/MoreVert'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiModel, AdminAiModelStatus } from '../api/adminAiProvidersApi'

interface AiModelStatusMenuProps {
  model: AdminAiModel
  providerId: string
}

type Feedback = { severity: 'success' | 'error'; message: string } | null

const STATUS_OPTIONS: AdminAiModelStatus[] = ['Available', 'Deprecated', 'Unavailable']

const CONFIRM_COPY: Record<AdminAiModelStatus, { title: string; body: string }> = {
  Available: {
    title: 'Mark this model Available?',
    body: 'End users will be able to select it again as soon as you confirm.',
  },
  Deprecated: {
    title: 'Mark this model Deprecated?',
    body: 'End users will no longer be able to select it. Conversations that already used it keep their history and attribution.',
  },
  Unavailable: {
    title: 'Mark this model Unavailable?',
    body: 'End users will no longer be able to select it. Conversations that already used it keep their history and attribution.',
  },
}

/**
 * specs/008-ai-model-catalog-management US2 — per-model status-change menu, confirm-gated
 * per FR-010. Mirrors AiProviderActionsMenu.tsx's pendingAction/CONFIRM_COPY/Snackbar
 * composition (FR-011 feedback on every success/error).
 */
export function AiModelStatusMenu({ model, providerId }: AiModelStatusMenuProps) {
  const queryClient = useQueryClient()
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)
  const [pendingStatus, setPendingStatus] = useState<AdminAiModelStatus | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)

  const closeMenu = () => setAnchorEl(null)

  const mutation = useMutation({
    mutationFn: (status: AdminAiModelStatus) => adminAiProvidersApi.updateModelStatus(model.id, status),
    onSuccess: (_, status) => {
      queryClient.invalidateQueries({ queryKey: ['admin', 'ai-providers', providerId, 'models'] })
      setFeedback({ severity: 'success', message: `${model.displayName} marked ${status}.` })
    },
    onError: (err: unknown) => {
      const message = err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.'
      setFeedback({ severity: 'error', message })
    },
  })

  const handleConfirm = () => {
    if (pendingStatus) mutation.mutate(pendingStatus)
    setPendingStatus(null)
  }

  return (
    <>
      <IconButton size="small" aria-label={`Change status for ${model.displayName}`} onClick={(e) => setAnchorEl(e.currentTarget)}>
        <MoreVertIcon fontSize="small" />
      </IconButton>
      <Menu anchorEl={anchorEl} open={Boolean(anchorEl)} onClose={closeMenu}>
        {STATUS_OPTIONS.filter((status) => status !== model.status).map((status) => (
          <MenuItem
            key={status}
            onClick={() => {
              closeMenu()
              setPendingStatus(status)
            }}
          >
            Mark {status}
          </MenuItem>
        ))}
      </Menu>

      <Dialog open={pendingStatus !== null} onClose={() => setPendingStatus(null)}>
        {pendingStatus && (
          <>
            <DialogTitle>{CONFIRM_COPY[pendingStatus].title}</DialogTitle>
            <DialogContent>
              <DialogContentText>{CONFIRM_COPY[pendingStatus].body}</DialogContentText>
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setPendingStatus(null)}>Cancel</Button>
              <Button onClick={handleConfirm} color={pendingStatus === 'Available' ? 'primary' : 'error'} variant="contained" autoFocus>
                Confirm
              </Button>
            </DialogActions>
          </>
        )}
      </Dialog>

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </>
  )
}
