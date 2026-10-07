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
import { useT } from '../../../i18n/useT'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AdminAiModel } from '../api/adminAiProvidersApi'

interface AiModelStatusMenuProps {
  model: AdminAiModel
  providerId: string
}

type Feedback = { severity: 'success' | 'error'; message: string } | null
type AvailabilityStatus = 'Available' | 'Unavailable'

/**
 * specs/008-ai-model-catalog-management US2 — per-model availability toggle, confirm-gated
 * per FR-010. A "Deprecated" model's toggle is disabled: re-enabling a vendor-retired model
 * isn't a simple flip back to Available — it needs its own review workflow (notify affected
 * users, reassign any default-model pointer), which is not yet built.
 */
export function AiModelStatusMenu({ model, providerId }: AiModelStatusMenuProps) {
  const t = useT('admin.aiProviders')
  const queryClient = useQueryClient()
  const [pendingStatus, setPendingStatus] = useState<AvailabilityStatus | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)

  const mutation = useMutation({
    mutationFn: (status: AvailabilityStatus) => adminAiProvidersApi.updateModelStatus(model.id, status),
    onSuccess: (_, status) => {
      queryClient.invalidateQueries({ queryKey: ['admin', 'ai-providers', providerId, 'models'] })
      setFeedback({ severity: 'success', message:
          status === 'Available'
            ? t('modelStatus.markedAvailable', { name: model.displayName })
            : t('modelStatus.markedUnavailable', { name: model.displayName }),
      })
    },
    onError: (err: unknown) => {
      const message = err instanceof ApiError ? err.detail ?? err.message : t('shared.genericError')
      setFeedback({ severity: 'error', message })
    },
  })

  const isDeprecated = model.status === 'Deprecated'
  const isAvailable = model.status === 'Available'
  const nextStatus: AvailabilityStatus = isAvailable ? 'Unavailable' : 'Available'
  const toggleLabel = isDeprecated
    ? t('modelStatus.deprecatedTooltip')
    : isAvailable
      ? t('modelStatus.markUnavailable')
      : t('modelStatus.markAvailable')
  const toggleAria = isDeprecated
    ? t('modelStatus.deprecatedFor', { name: model.displayName })
    : isAvailable
      ? t('modelStatus.markUnavailableFor', { name: model.displayName })
      : t('modelStatus.markAvailableFor', { name: model.displayName })
  const confirmCopy = {
    Available: {
      title: t('modelStatus.confirmAvailableTitle'),
      body: t('modelStatus.confirmAvailableBody'),
    },
    Unavailable: {
      title: t('modelStatus.confirmUnavailableTitle'),
      body: t('modelStatus.confirmUnavailableBody'),
    },
  }

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
            aria-label={toggleAria}
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
            <DialogTitle>{confirmCopy[pendingStatus].title}</DialogTitle>
            <DialogContent>
              <DialogContentText>{confirmCopy[pendingStatus].body}</DialogContentText>
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setPendingStatus(null)}>{t('shared.cancel')}</Button>
              <Button onClick={handleConfirm} color={pendingStatus === 'Available' ? 'primary' : 'error'} variant="contained" autoFocus>
                {t('shared.confirm')}
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
