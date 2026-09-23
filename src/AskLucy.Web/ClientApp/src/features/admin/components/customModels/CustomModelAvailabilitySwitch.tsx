import { useState } from 'react'
import { Alert, Snackbar, Switch, Tooltip } from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ConfirmDialog } from '../../../../components/ConfirmDialog'
import * as customModelsApi from '../../api/adminCustomModelsApi'
import type { Availability, CustomModelSummary } from '../../api/adminCustomModelsApi'
import { errorMessage } from './errorMessage'

type Feedback = { severity: 'success' | 'error'; message: string } | null

const CONFIRM_COPY: Record<Availability, { title: (name: string) => string; body: string; label: string }> = {
  Available: {
    title: (name) => `Make ${name} available?`,
    body: 'The engine that runs from this repository will use this model. Only one model per repository can be available.',
    label: 'Make available',
  },
  Unavailable: {
    title: (name) => `Make ${name} unavailable?`,
    body: 'The engine that runs from this repository will stop until another model for it is made available.',
    label: 'Make unavailable',
  },
}

/**
 * specs/072 FR-030 — a model's availability switch, confirm-gated like `AiModelStatusMenu`. When the
 * server says the model can't be made available, the switch is disabled and its reason is the tooltip.
 */
export function CustomModelAvailabilitySwitch({ model }: { model: CustomModelSummary }) {
  const queryClient = useQueryClient()
  const [pending, setPending] = useState<Availability | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)

  const mutation = useMutation({
    mutationFn: (availability: Availability) => customModelsApi.setCustomModelAvailability(model.id, availability),
    onSuccess: (_, availability) => {
      void queryClient.invalidateQueries({ queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.all })
      setFeedback({ severity: 'success', message: `${model.name} is now ${availability.toLowerCase()}.` })
    },
    onError: (err: unknown) => setFeedback({ severity: 'error', message: errorMessage(err) }),
  })

  const isAvailable = model.availability === 'Available'
  const blocked = !isAvailable && !model.canMakeAvailable
  const blockedReason = blocked ? model.availabilityBlockedReason ?? 'This model cannot be made available.' : ''

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
            slotProps={{ input: { 'aria-label': `${model.name} available` } }}
          />
        </span>
      </Tooltip>
      <ConfirmDialog
        open={pending !== null}
        title={pending ? CONFIRM_COPY[pending].title(model.name) : ''}
        description={pending ? CONFIRM_COPY[pending].body : ''}
        confirmLabel={pending ? CONFIRM_COPY[pending].label : undefined}
        destructive={pending === 'Unavailable'}
        onConfirm={confirm}
        onCancel={() => setPending(null)}
      />
      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </>
  )
}
