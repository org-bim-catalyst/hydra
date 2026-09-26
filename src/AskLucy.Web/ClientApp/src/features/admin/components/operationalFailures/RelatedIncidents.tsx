import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Link,
  List,
  ListItem,
  ListItemButton,
  ListItemText,
  Pagination,
  Stack,
  Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as operationalFailuresApi from '../../api/adminOperationalFailuresApi'
import type { BulkTransitionResult, IncidentDetail } from '../../api/adminOperationalFailuresApi'
import { ResolveNoteDialog } from './ResolveNoteDialog'
import { TransitionFeedbackSnackbar } from './TransitionFeedbackSnackbar'
import { describeBulkResult, describeTransitionError } from './transitionFeedback'
import type { TransitionFeedback } from './transitionFeedback'
import { engineLabel, formatWhen, kindLabel, severityColor } from './operationalFailureLabels'

const RELATED_PAGE_SIZE = 10

type BulkAction = { type: 'acknowledge' } | { type: 'resolve'; note: string | null }

const BULK_VERB: Record<BulkAction['type'], string> = { acknowledge: 'Acknowledged', resolve: 'Resolved' }

const pluralIncidents = (count: number) => (count === 1 ? '1 other incident shares' : `${count} other incidents share`)

/**
 * specs/074 FR-026a/b — the other unresolved incidents with this one's root cause, and, for holders of
 * *Manage operational failures*, Acknowledge all / Resolve all over every one of them (this one included).
 * Each incident succeeds, is skipped or fails on its own; the failures stay listed here.
 */
export function RelatedIncidents({
  incident,
  onOpenIncident,
}: {
  incident: IncidentDetail
  onOpenIncident: (id: string) => void
}) {
  const queryClient = useQueryClient()
  const [expanded, setExpanded] = useState(false)
  const [page, setPage] = useState(1)
  const [noteOpen, setNoteOpen] = useState(false)
  const [dialogError, setDialogError] = useState<string | null>(null)
  const [feedback, setFeedback] = useState<TransitionFeedback | null>(null)
  const [failed, setFailed] = useState<BulkTransitionResult['failed']>([])

  const related = useQuery({
    queryKey: operationalFailuresApi.OPERATIONAL_FAILURE_QUERY_KEYS.related(incident.id, page, RELATED_PAGE_SIZE),
    queryFn: () => operationalFailuresApi.getRelatedIncidents(incident.id, page, RELATED_PAGE_SIZE),
    enabled: expanded,
  })

  const bulk = useMutation({
    mutationFn: (action: BulkAction) =>
      action.type === 'acknowledge'
        ? operationalFailuresApi.acknowledgeRootCause(incident.rootCauseKey)
        : operationalFailuresApi.resolveRootCause(incident.rootCauseKey, action.note),
    onSuccess: (result, action) => {
      // invalidateQueries never rejects; a failed refetch shows on the query that made it.
      void queryClient.invalidateQueries({ queryKey: operationalFailuresApi.OPERATIONAL_FAILURE_QUERY_KEYS.all })
      setNoteOpen(false)
      setFailed(result.failed)
      setFeedback({
        severity: result.failed.length > 0 ? 'warning' : 'success',
        text: describeBulkResult(BULK_VERB[action.type], result),
      })
    },
    onError: (error, action) => {
      const described = describeTransitionError(error)
      if (action.type === 'resolve') setDialogError(described.text)
      else setFeedback(described)
    },
  })

  if (incident.relatedOpenCount === 0 && failed.length === 0) return null

  const totalPages = related.data ? Math.max(1, Math.ceil(related.data.totalCount / RELATED_PAGE_SIZE)) : 1

  return (
    <Box>
      {incident.relatedOpenCount > 0 && (
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
          <Link component="button" variant="body2" aria-expanded={expanded} onClick={() => setExpanded((open) => !open)}>
            {pluralIncidents(incident.relatedOpenCount)} this cause
          </Link>
          {incident.canManage && (
            <>
              <Button size="small" disabled={bulk.isPending} onClick={() => bulk.mutate({ type: 'acknowledge' })}>
                Acknowledge all
              </Button>
              <Button
                size="small"
                disabled={bulk.isPending}
                onClick={() => {
                  setDialogError(null)
                  setNoteOpen(true)
                }}
              >
                Resolve all
              </Button>
            </>
          )}
        </Stack>
      )}

      {expanded && (
        <Box sx={{ mt: 1 }}>
          {related.isLoading && <CircularProgress size={20} aria-label="Loading related incidents" />}
          {related.isError && (
            <Alert
              severity="error"
              action={
                <Button color="inherit" size="small" onClick={() => void related.refetch()}>
                  Retry
                </Button>
              }
            >
              Could not load the related incidents.
            </Alert>
          )}
          {related.data && (
            <>
              <List dense aria-label="Incidents sharing this cause">
                {related.data.items.map((item) => (
                  <ListItem key={item.id} disablePadding>
                    <ListItemButton onClick={() => onOpenIncident(item.id)}>
                      <ListItemText
                        primary={
                          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                            <Chip size="small" variant="outlined" label={item.severity} color={severityColor(item.severity)} />
                            <span>{item.subject?.label ?? item.operation}</span>
                          </Stack>
                        }
                        secondary={`${engineLabel(item.engine)} · ${kindLabel(item.kind)} · ${item.state} · last ${formatWhen(item.lastSeenUtc)}`}
                      />
                    </ListItemButton>
                  </ListItem>
                ))}
              </List>
              {totalPages > 1 && (
                <Pagination size="small" count={totalPages} page={page} onChange={(_, next) => setPage(next)} />
              )}
            </>
          )}
        </Box>
      )}

      {failed.length > 0 && (
        <Alert severity="warning" sx={{ mt: 1 }} onClose={() => setFailed([])}>
          <Typography variant="body2">These incidents could not be updated:</Typography>
          <Box component="ul" sx={{ m: 0, pl: 2 }}>
            {failed.map((failure) => (
              <li key={failure.incidentId}>
                <Link component="button" variant="body2" onClick={() => onOpenIncident(failure.incidentId)}>
                  {failure.incidentId}
                </Link>{' '}
                — {failure.reason}
              </li>
            ))}
          </Box>
        </Alert>
      )}

      <ResolveNoteDialog
        open={noteOpen}
        title="Resolve every incident with this cause"
        confirmLabel="Resolve all"
        pending={bulk.isPending}
        errorMessage={dialogError}
        onCancel={() => setNoteOpen(false)}
        onConfirm={(note) => bulk.mutate({ type: 'resolve', note })}
      />
      <TransitionFeedbackSnackbar feedback={feedback} onClose={() => setFeedback(null)} onOpenIncident={onOpenIncident} />
    </Box>
  )
}
