import { useState } from 'react'
import { Alert, Snackbar, Switch, Tooltip } from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useT } from '../../../../i18n/useT'
import { ConfirmDialog } from '../../../../components/ConfirmDialog'
import * as customModelsApi from '../../api/adminCustomModelsApi'
import type { Availability, CustomModelSummary } from '../../api/adminCustomModelsApi'
import { errorMessage } from './errorMessage'

type Feedback = { severity: 'success' | 'error'; message: string } | null

/**
 * specs/072 FR-030 — a model's availability switch, confirm-gated like `AiModelStatusMenu`. When the
 * server says the model can't be made available, the switch is disabled and its reason is the tooltip.
 */
export function CustomModelAvailabilitySwitch({ model }: { model: CustomModelSummary }) {
  const t = useT('admin.aiProviders')
  const queryClient = useQueryClient()
  const [pending, setPending] = useState<Availability | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)

  const mutation = useMutation({
    mutationFn: (availability: Availability) =>
      customModelsApi.setCustomModelAvailability(model.id, availability),
    onSuccess: (_, availability) => {
      void queryClient.invalidateQueries({ queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.all })
      setFeedback({
        severity: 'success',
        message:
          availability === 'Available'
            ? t('availabilitySwitch.nowAvailable', { name: model.name })
            : t('availabilitySwitch.nowUnavailable', { name: model.name }),
      })
    },
    onError: (err: unknown) => setFeedback({ severity: 'error', message: errorMessage(err, t) }),
  })

  const isAvailable = model.availability === 'Available'
  const blocked = !isAvailable && !model.canMakeAvailable
  const blockedReason = blocked
    ? (model.availabilityBlockedReason ?? t('availabilitySwitch.blockedFallback'))
    : ''

  const confirmCopy = {
    Available: {
      title: t('availabilitySwitch.makeAvailableTitle', { name: model.name }),
      body: t('availabilitySwitch.makeAvailableBody'),
      label: t('availabilitySwitch.makeAvailableLabel'),
    },
    Unavailable: {
      title: t('availabilitySwitch.makeUnavailableTitle', { name: model.name }),
      body: t('availabilitySwitch.makeUnavailableBody'),
      label: t('availabilitySwitch.makeUnavailableLabel'),
    },
  }

  const confirm = () => {
    if (pending) mutation.mutate(pending)
    setPending(null)
  }

  return (
    <>
      <Tooltip title={blockedReason} describeChild>
        <span>
          <Switch
            size="small"
            checked={isAvailable}
            disabled={blocked || mutation.isPending}
            onChange={() => setPending(isAvailable ? 'Unavailable' : 'Available')}
            slotProps={{
              input: { 'aria-label': t('availabilitySwitch.switchLabel', { name: model.name }) },
            }}
          />
        </span>
      </Tooltip>
      <ConfirmDialog
        open={pending !== null}
        title={pending ? confirmCopy[pending].title : ''}
        description={pending ? confirmCopy[pending].body : ''}
        confirmLabel={pending ? confirmCopy[pending].label : undefined}
        cancelLabel={t('shared.cancel')}
        destructive={pending === 'Unavailable'}
        onConfirm={confirm}
        onCancel={() => setPending(null)}
      />
      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert
          severity={feedback?.severity ?? 'info'}
          variant="filled"
          onClose={() => setFeedback(null)}
        >
          {feedback?.message}
        </Alert>
      </Snackbar>
    </>
  )
}
