import { useState } from 'react'
import { Alert, Box, Button, LinearProgress, Snackbar, Stack, Typography } from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useT } from '../../../../i18n/useT'
import { ConfirmDialog } from '../../../../components/ConfirmDialog'
import * as customModelsApi from '../../api/adminCustomModelsApi'
import type { CustomModelSummary, TransferPhase } from '../../api/adminCustomModelsApi'
import { formatBytes } from './formatBytes'
import { errorMessage } from './errorMessage'

interface CustomModelProgressProps {
  model: CustomModelSummary
  canManage: boolean
  /** The latest phase from the hub; absent until the first progress event after a load. */
  phase?: TransferPhase
}

const percent = (done: number, total: number) =>
  total > 0 ? Math.min(100, Math.round((done / total) * 100)) : 0

/** specs/072 US2 — overall and current-file progress for one in-progress deployment, with Cancel. */
export function CustomModelProgress({ model, canManage, phase }: CustomModelProgressProps) {
  const t = useT('admin.aiProviders')
  const queryClient = useQueryClient()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [toast, setToast] = useState<string | null>(null)

  const cancelMutation = useMutation({
    mutationFn: () => customModelsApi.cancelCustomModelDeployment(model.id),
    // The hub pushes the new state too; this covers a disconnected hub.
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.all }),
    onError: (err) => setToast(errorMessage(err, t)),
  })

  const sized = model.deploymentState === 'Transferring' && model.totalBytes !== null
  const fileTotal = model.currentFileTotalBytes
  const bytesLabel = t('shared.bytes')

  return (
    <Stack spacing={0.5} sx={{ minWidth: 220 }}>
      <LinearProgress
        aria-label={t('progress.overall', { name: model.name })}
        variant={sized ? 'determinate' : 'indeterminate'}
        value={sized ? percent(model.transferredBytes, model.totalBytes ?? 0) : undefined}
      />
      <Typography variant="caption" color="text.secondary">
        {model.deploymentState === 'Queued' && t('progress.waiting')}
        {model.deploymentState === 'Listing' && t('progress.listing')}
        {sized &&
          t('progress.sized', {
            done: formatBytes(model.transferredBytes, bytesLabel),
            total: formatBytes(model.totalBytes ?? 0, bytesLabel),
            completed: model.completedFileCount,
            count: model.totalFileCount ?? 0,
          })}
      </Typography>
      {model.deploymentState === 'Transferring' && model.currentFilePath !== null && (
        <>
          <LinearProgress
            aria-label={t('progress.fileProgress', { path: model.currentFilePath })}
            color="secondary"
            variant={fileTotal ? 'determinate' : 'indeterminate'}
            value={fileTotal ? percent(model.currentFileBytes ?? 0, fileTotal) : undefined}
          />
          <Typography variant="caption" color="text.secondary" sx={{ wordBreak: 'break-all' }}>
            {t('progress.phaseFile', {
              phase: t(`progress.phase.${phase ?? 'Transferring'}`),
              path: model.currentFilePath,
            })}
          </Typography>
        </>
      )}
      {canManage && model.canCancel && (
        <Box>
          <Button
            size="small"
            color="error"
            aria-label={t('progress.cancelFor', { name: model.name })}
            disabled={cancelMutation.isPending}
            onClick={() => setConfirmOpen(true)}
          >
            {t('shared.cancel')}
          </Button>
        </Box>
      )}
      <ConfirmDialog
        open={confirmOpen}
        title={t('progress.cancelTitle')}
        description={t('progress.cancelBody', { name: model.name })}
        confirmLabel={t('progress.cancelConfirm')}
        cancelLabel={t('progress.keepRunning')}
        onConfirm={() => {
          setConfirmOpen(false)
          cancelMutation.mutate()
        }}
        onCancel={() => setConfirmOpen(false)}
      />
      <Snackbar open={toast !== null} autoHideDuration={6000} onClose={() => setToast(null)}>
        <Alert severity="error" variant="filled" onClose={() => setToast(null)}>
          {toast}
        </Alert>
      </Snackbar>
    </Stack>
  )
}
