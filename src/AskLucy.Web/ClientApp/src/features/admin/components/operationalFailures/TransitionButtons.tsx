import { useState } from 'react'
import { Button, Stack } from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import * as operationalFailuresApi from '../../api/adminOperationalFailuresApi'
import type { IncidentDetail } from '../../api/adminOperationalFailuresApi'
import { ResolveNoteDialog } from './ResolveNoteDialog'
import { TransitionFeedbackSnackbar } from './TransitionFeedbackSnackbar'
import { describeTransitionError, isIncidentConflict } from './transitionFeedback'
import type { TransitionFeedback } from './transitionFeedback'

type Transition = { type: 'acknowledge' } | { type: 'resolve'; note: string | null } | { type: 'reopen' }

const SUCCESS_TEXT: Record<Transition['type'], string> = {
  acknowledge: 'Incident acknowledged.',
  resolve: 'Incident resolved.',
  reopen: 'Incident reopened.',
}

/**
 * specs/074 FR-024 — Acknowledge (Open), Resolve (Open or Acknowledged, with an optional note) and
 * Reopen (Resolved). Hidden without *Manage operational failures*; the server checks it again.
 * Every outcome refreshes the list, the detail and the nav badge.
 */
export function TransitionButtons({
  incident,
  onOpenIncident,
}: {
  incident: IncidentDetail
  onOpenIncident: (id: string) => void
}) {
  const queryClient = useQueryClient()
  const [noteOpen, setNoteOpen] = useState(false)
  const [dialogError, setDialogError] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<TransitionFeedback | null>(null)

  // invalidateQueries never rejects; a failed refetch shows on the query that made it.
  const refresh = () => void queryClient.invalidateQueries({ queryKey: operationalFailuresApi.OPERATIONAL_FAILURE_QUERY_KEYS.all })

  const mutation = useMutation({
    mutationFn: (transition: Transition) => {
      switch (transition.type) {
        case 'acknowledge':
          return operationalFailuresApi.acknowledgeIncident(incident.id)
        case 'resolve':
          return operationalFailuresApi.resolveIncident(incident.id, transition.note)
        case 'reopen':
          return operationalFailuresApi.reopenIncident(incident.id)
      }
    },
    onSuccess: (updated, transition) => {
      queryClient.setQueryData(operationalFailuresApi.OPERATIONAL_FAILURE_QUERY_KEYS.incident(updated.id), updated)
      refresh()
      setNoteOpen(false)
      setFeedback({ severity: 'success', text: SUCCESS_TEXT[transition.type] })
    },
    onError: (error, transition) => {
      const described = describeTransitionError(error)
      if (isIncidentConflict(error)) {
        refresh()
        setNoteOpen(false)
        setFeedback(described)
      } else if (transition.type === 'resolve') {
        // Keep the dialog, and the note typed into it, so a retry does not mean retyping it.
        setDialogError(described.text)
      } else {
        setFeedback(described)
      }
    },
  })

  if (!incident.canManage) return null

  return (
    <>
      <Stack direction="row" spacing={1} role="group" aria-label="Incident actions">
        {incident.state === 'Open' && (
          <Button size="small" variant="outlined" disabled={mutation.isPending} onClick={() => mutation.mutate({ type: 'acknowledge' })}>
            Acknowledge
          </Button>
        )}
        {incident.state !== 'Resolved' && (
          <Button
            size="small"
            variant="contained"
            disabled={mutation.isPending}
            onClick={() => {
              setDialogError(null)
              setNoteOpen(true)
            }}
          >
            Resolve
          </Button>
        )}
        {incident.state === 'Resolved' && (
          <Button size="small" variant="outlined" disabled={mutation.isPending} onClick={() => mutation.mutate({ type: 'reopen' })}>
            Reopen
          </Button>
        )}
      </Stack>
      <ResolveNoteDialog
        open={noteOpen}
        title="Resolve incident"
        confirmLabel="Resolve"
        pending={mutation.isPending}
        errorMessage={dialogError}
        onCancel={() => setNoteOpen(false)}
        onConfirm={(note) => mutation.mutate({ type: 'resolve', note })}
      />
      <TransitionFeedbackSnackbar feedback={feedback} onClose={() => setFeedback(null)} onOpenIncident={onOpenIncident} />
    </>
  )
}
