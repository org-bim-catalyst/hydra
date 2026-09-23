import { useState } from 'react'
import { Alert, IconButton, Snackbar, Tooltip } from '@mui/material'
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ConfirmDialog } from '../../../../components/ConfirmDialog'
import * as customModelsApi from '../../api/adminCustomModelsApi'
import type { CustomModelSummary } from '../../api/adminCustomModelsApi'
import { errorMessage } from './errorMessage'

/**
 * specs/072 FR-031 — removes a failed or cancelled model's record after confirmation. The row
 * disappearing is the success feedback; a failure keeps the row and shows why.
 */
export function RemoveCustomModelButton({ model }: { model: CustomModelSummary }) {
  const queryClient = useQueryClient()
  const [confirming, setConfirming] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: () => customModelsApi.removeCustomModel(model.id),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.all }),
    onError: (err: unknown) => setError(errorMessage(err)),
  })

  const confirm = () => {
    setConfirming(false)
    mutation.mutate()
  }

  return (
    <>
      <Tooltip title="Remove">
        <span>
          <IconButton
            size="small"
            aria-label={`Remove ${model.name}`}
            disabled={mutation.isPending}
            onClick={() => setConfirming(true)}
          >
            <DeleteOutlineIcon fontSize="small" />
          </IconButton>
        </span>
      </Tooltip>
      <ConfirmDialog
        open={confirming}
        title={`Remove ${model.name}?`}
        description="The record is removed and its name can be used again. Any files it already uploaded stay on the deployment target."
        confirmLabel="Remove"
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
