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
  Snackbar,
  Tooltip,
} from '@mui/material'
import PowerSettingsNewIcon from '@mui/icons-material/PowerSettingsNew'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiModel } from '../api/adminAiProvidersApi'

interface AiModelStatusMenuProps {
  model: AdminAiModel
  providerId: string
}

type Feedback = { severity: 'success' | 'error'; message: string } | null
type AvailabilityStatus = 'Available' | 'Unavailable'

const CONFIRM_COPY: Record<AvailabilityStatus, { title: string; body: string }> = {
  Available: {
    title: 'Mark this model Available?',
    body: 'End users will be able to select it again as soon as you confirm.',
  },
  Unavailable: {
    title: 'Mark this model Unavailable?',
    body: 'End users will no longer be able to select it. Conversations that already used it keep their history and attribution.',
  },
}

/**
 * specs/008-ai-model-catalog-management US2 — per-model availability toggle, confirm-gated
 * per FR-010. A "Deprecated" model's toggle is disabled: re-enabling a vendor-retired model
 * isn't a simple flip back to Available — it needs its own review workflow (notify affected
 * users, reassign any default-model pointer), which is not yet built.
 */
export function AiModelStatusMenu({ model, providerId }: AiModelStatusMenuProps) {
  const queryClient = useQueryClient()
  const [pendingStatus, setPendingStatus] = useState<AvailabilityStatus | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)

  const mutation = useMutation({
    mutationFn: (status: AvailabilityStatus) => adminAiProvidersApi.updateModelStatus(model.id, status),
    onSuccess: (_, status) => {
      queryClient.invalidateQueries({ queryKey: ['admin', 'ai-providers', providerId, 'models'] })
      setFeedback({ severity: 'success', message: `${model.displayName} marked ${status}.` })
    },
    onError: (err: unknown) => {
      const message = err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.'
      setFeedback({ severity: 'error', message })
    },
  })

  const isDeprecated = model.status === 'Deprecated'
  const isAvailable = model.status === 'Available'
  const nextStatus: AvailabilityStatus = isAvailable ? 'Unavailable' : 'Available'
  const toggleLabel = isDeprecated
    ? 'Deprecated by the vendor — cannot be re-enabled from here'
    : isAvailable
      ? 'Mark unavailable'
      : 'Mark available'

  const handleConfirm = () => {
    if (pendingStatus) mutation.mutate(pendingStatus)
    setPendingStatus(null)
  }

  return (
    <>
      <Tooltip title={toggleLabel}>
        <span>
          <IconButton
            size="small"
            aria-label={`${toggleLabel} for ${model.displayName}`}
            disabled={isDeprecated}
            onClick={() => setPendingStatus(nextStatus)}
          >
            <PowerSettingsNewIcon
              fontSize="small"
              color={isAvailable ? 'success' : 'inherit'}
              sx={isAvailable ? undefined : { opacity: 0.4 }}
            />
          </IconButton>
        </span>
      </Tooltip>

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
