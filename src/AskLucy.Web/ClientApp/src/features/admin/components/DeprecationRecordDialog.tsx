import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  MenuItem,
  TextField,
  Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useT } from '../../../i18n/useT'
import { getModels } from '../api/adminAiProvidersApi'
import {
  acceptModelReplacement,
  changeModelReplacement,
  getModelDeprecation,
} from '../api/modelDeprecationsApi'

interface DeprecationRecordDialogProps {
  deprecationId: string
  providerId: string
  open: boolean
  onClose: () => void
}

/**
 * specs/069 FR-011a/FR-011b — the deprecation record and its validation actions. The replacement
 * is already in use; validating it either accepts it or names a different Available model from
 * the same provider.
 */
export function DeprecationRecordDialog({ deprecationId, providerId, open, onClose }: DeprecationRecordDialogProps) {
  const t = useT('admin.aiProviders')
  const queryClient = useQueryClient()
  const [chosenModelId, setChosenModelId] = useState('')
  const [feedback, setFeedback] = useState<{ severity: 'success' | 'error'; message: string } | null>(null)

  const detail = useQuery({
    queryKey: ['admin', 'model-deprecations', deprecationId],
    queryFn: () => getModelDeprecation(deprecationId),
    enabled: open,
  })

  const models = useQuery({
    queryKey: ['admin', 'ai-provider-models', providerId],
    queryFn: () => getModels(providerId),
    enabled: open,
  })

  const refresh = () => {
    void queryClient.invalidateQueries({ queryKey: ['admin', 'model-deprecations'] })
  }

  const accept = useMutation({
    mutationFn: () => acceptModelReplacement(deprecationId),
    onSuccess: () => {
      setFeedback({ severity: 'success', message: t('record.accepted') })
      refresh()
    },
    onError: (error: unknown) => setFeedback({
      severity: 'error',
      message: error instanceof Error ? error.message : t('record.failed'),
    }),
  })

  const change = useMutation({
    mutationFn: () => changeModelReplacement(deprecationId, chosenModelId),
    onSuccess: () => {
      setFeedback({ severity: 'success', message: t('record.changed') })
      refresh()
    },
    onError: (error: unknown) => setFeedback({
      severity: 'error',
      message: error instanceof Error ? error.message : t('record.failed'),
    }),
  })

  const record = detail.data
  const canValidate = record?.validationStatus === 'AwaitingValidation'
  const candidates = (models.data ?? []).filter((model) => model.status === 'Available')

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('record.title')}</DialogTitle>
      <DialogContent>
        {detail.isError && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {detail.error instanceof Error ? detail.error.message : t('record.failed')}
          </Alert>
        )}
        {record && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
            <Typography variant="body2">
              {record.displayName} · {record.providerName}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {t('record.replacement', { name: record.replacementDisplayName ?? t('record.none') })}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {t('record.validation', { status: record.validationStatus })}
            </Typography>
            {record.platformDefaults.map((change, index) => (
              <Typography key={`${change.targetKind}-${change.capability ?? ''}-${index}`} variant="caption" color={change.isOpenFlag ? 'warning.main' : 'text.secondary'}>
                {change.capability ?? change.targetKind}: {change.outcome}
                {change.newModelDisplayName ? ` → ${change.newModelDisplayName}` : ''}
              </Typography>
            ))}
            {canValidate && (
              <>
                <Alert severity="info">{t('record.ask')}</Alert>
                <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
                  <Button variant="contained" disabled={accept.isPending} onClick={() => accept.mutate()}>
                    {t('record.accept')}
                  </Button>
                  <TextField
                    select
                    size="small"
                    label={t('record.changeTo')}
                    value={chosenModelId}
                    onChange={(event) => setChosenModelId(event.target.value)}
                    sx={{ minWidth: 220 }}
                  >
                    {candidates.map((model) => (
                      <MenuItem key={model.id} value={model.id} disabled={model.id === record.replacementModelId}>
                        {model.displayName}
                      </MenuItem>
                    ))}
                  </TextField>
                  <Button
                    disabled={!chosenModelId || change.isPending}
                    onClick={() => change.mutate()}
                  >
                    {t('record.change')}
                  </Button>
                </Box>
              </>
            )}
            {feedback && <Alert severity={feedback.severity}>{feedback.message}</Alert>}
          </Box>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('shared.close')}</Button>
      </DialogActions>
    </Dialog>
  )
}
