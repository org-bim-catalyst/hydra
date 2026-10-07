import { useState } from 'react'
import { Alert, IconButton, Snackbar, Tooltip } from '@mui/material'
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useT } from '../../../../i18n/useT'
import { ConfirmDialog } from '../../../../components/ConfirmDialog'
import * as customModelsApi from '../../api/adminCustomModelsApi'
import type { CustomModelSummary } from '../../api/adminCustomModelsApi'
import { errorMessage } from './errorMessage'

/**
 * specs/072 FR-031 — removes a failed or cancelled model's record after confirmation. The row
 * disappearing is the success feedback; a failure keeps the row and shows why. specs/078 FR-009b:
 * the deployment Local Whisper uses stays disabled until another model is selected.
 */
export function RemoveCustomModelButton({ model }: { model: CustomModelSummary }) {
  const t = useT('admin.aiProviders')
  const queryClient = useQueryClient()
  const [confirming, setConfirming] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: () => customModelsApi.removeCustomModel(model.id),
    onSuccess: () =>
      void queryClient.invalidateQueries({
        queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.all,
      }),
    onError: (err: unknown) => setError(errorMessage(err, t)),
  })

  const confirm = () => {
    setConfirming(false)
    mutation.mutate()
  }

  return (
    <>
      <Tooltip
        title={model.selectedForLocalWhisper ? t('remove.blockedWhisper') : t('remove.tooltip')}
      >
        <span>
          <IconButton
            size="small"
            aria-label={t('remove.removeFor', { name: model.name })}
            disabled={mutation.isPending || model.selectedForLocalWhisper}
            onClick={() => setConfirming(true)}
          >
            <DeleteOutlineIcon fontSize="small" />
          </IconButton>
        </span>
      </Tooltip>
      <ConfirmDialog
        open={confirming}
        title={t('remove.confirmTitle', { name: model.name })}
        description={t('remove.confirmBody')}
        confirmLabel={t('remove.confirmLabel')}
        cancelLabel={t('shared.cancel')}
        onConfirm={confirm}
        onCancel={() => setConfirming(false)}
      />
      <Snackbar open={error !== null} autoHideDuration={5000} onClose={() => setError(null)}>
        <Alert severity="error" variant="filled" onClose={() => setError(null)}>
          {error}
        </Alert>
      </Snackbar>
    </>
  )
}
