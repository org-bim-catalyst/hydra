import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Drawer,
  IconButton,
  Link,
  Stack,
  Tooltip,
  Typography,
} from '@mui/material'
import CloseIcon from '@mui/icons-material/Close'
import ContentCopyIcon from '@mui/icons-material/ContentCopy'
import { useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { codeFontFamily } from '../../../../theme/tokens/typography'
import * as operationalFailuresApi from '../../api/adminOperationalFailuresApi'
import type { IncidentDetail } from '../../api/adminOperationalFailuresApi'
import { CorrectiveActionLink } from './CorrectiveActionLink'
import { OccurrenceTable } from './OccurrenceTable'
import { engineLabel, formatWhen, kindLabel, severityColor } from './operationalFailureLabels'
import { UserRefLink } from './UserRefLink'

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Box>
      <Typography variant="caption" color="text.secondary" component="div">
        {label}
      </Typography>
      <Box sx={{ typography: 'body2' }}>{children}</Box>
    </Box>
  )
}

/** Copies the correlation id so it can be pasted into a server-log search; the outcome is always said. */
function CopyCorrelationId({ value }: { value: string }) {
  const [status, setStatus] = useState<'idle' | 'copied' | 'failed'>('idle')

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setStatus('copied')
    } catch {
      setStatus('failed')
    }
  }

  return (
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
      <Typography variant="body2" sx={{ fontFamily: codeFontFamily, wordBreak: 'break-all' }}>
        {value}
      </Typography>
      <Tooltip title={status === 'copied' ? 'Copied' : status === 'failed' ? 'Could not copy — select the id instead' : 'Copy'}>
        <IconButton size="small" aria-label="Copy correlation id" onClick={() => void copy()}>
          <ContentCopyIcon fontSize="inherit" />
        </IconButton>
      </Tooltip>
      {status === 'failed' && (
        <Typography variant="caption" color="error">
          Could not copy.
        </Typography>
      )}
    </Stack>
  )
}

function IncidentBody({ incident, onOpenIncident }: { incident: IncidentDetail; onOpenIncident: (id: string) => void }) {
  const isAccess = incident.engine === 'Access'

  return (
    <Stack spacing={2}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
        <Chip size="small" label={incident.severity} color={severityColor(incident.severity)} variant="outlined" />
        <Chip size="small" label={incident.state} variant="outlined" />
        <Typography variant="body2" color="text.secondary">
          {engineLabel(incident.engine)} · {kindLabel(incident.kind)}
        </Typography>
      </Stack>

      <Field label="Reason">{incident.latestReason}</Field>
      <Field label="Correlation id">
        <CopyCorrelationId value={incident.latestCorrelationId} />
      </Field>
      {(incident.providerName || incident.model) && (
        <Field label="Provider / model">{[incident.providerName, incident.model].filter(Boolean).join(' · ')}</Field>
      )}
      {incident.providerHealth && (
        <Field label="Provider health">
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <Chip
              size="small"
              variant="outlined"
              label={incident.providerHealth.status}
              color={incident.providerHealth.status === 'Healthy' ? 'success' : incident.providerHealth.status === 'Unhealthy' ? 'error' : 'default'}
            />
            {incident.providerHealth.checkedAtUtc && (
              <Typography variant="caption" color="text.secondary">
                checked {formatWhen(incident.providerHealth.checkedAtUtc)}
              </Typography>
            )}
          </Stack>
        </Field>
      )}
      <Field label="Seen">
        {formatWhen(incident.firstSeenUtc)} – {formatWhen(incident.lastSeenUtc)}
      </Field>
      <Field label={isAccess ? 'Reach' : 'Occurrences'}>
        {isAccess
          ? `${incident.distinctSourceCount} sources · ${incident.distinctUserCount} accounts`
          : `${incident.occurrenceCount} occurrences · ${incident.distinctUserCount} users`}
        {incident.recoveryCount > 0 && ` · recovered ${incident.recoveryCount}×`}
      </Field>
      {incident.recurrenceOfIncidentId && (
        <Field label="History">
          <Link component="button" variant="body2" onClick={() => onOpenIncident(incident.recurrenceOfIncidentId!)}>
            Recurrence of an earlier resolved incident
          </Link>
        </Field>
      )}
      {incident.acknowledged && (
        <Field label="Acknowledged">
          <UserRefLink user={incident.acknowledged.by} /> · {formatWhen(incident.acknowledged.atUtc)}
        </Field>
      )}
      {incident.resolved && (
        <Field label="Resolved">
          <UserRefLink user={incident.resolved.by} /> · {formatWhen(incident.resolved.atUtc)}
          {incident.resolved.note && <Box sx={{ mt: 0.5 }}>{incident.resolved.note}</Box>}
        </Field>
      )}
      <Field label="Suggested action">
        <CorrectiveActionLink action={incident.correctiveAction} />
      </Field>
      {incident.sampleUsers.length > 0 && (
        <Field label="Recent users">
          <Stack direction="row" spacing={1.5} sx={{ flexWrap: 'wrap' }}>
            {incident.sampleUsers.map((user, index) => (
              <UserRefLink key={user.id ?? `erased-${index}`} user={user} />
            ))}
          </Stack>
        </Field>
      )}

      <Divider />
      <Typography variant="subtitle2" component="h3">
        Occurrences
      </Typography>
      <OccurrenceTable incidentId={incident.id} showSource={isAccess} />
    </Stack>
  )
}

/**
 * specs/074 FR-012/FR-015/FR-017 — one incident's detail, its fix, and the occurrences behind it.
 * A recurrence links back to the resolved incident it follows, opened in the same drawer.
 */
export function IncidentDrawer({
  incidentId,
  onClose,
  onOpenIncident,
}: {
  incidentId: string | null
  onClose: () => void
  onOpenIncident: (id: string) => void
}) {
  const query = useQuery({
    queryKey: operationalFailuresApi.OPERATIONAL_FAILURE_QUERY_KEYS.incident(incidentId ?? ''),
    queryFn: () => operationalFailuresApi.getIncident(incidentId!),
    enabled: incidentId !== null,
  })

  return (
    <Drawer anchor="right" open={incidentId !== null} onClose={onClose}>
      <Box sx={{ width: { xs: '100vw', sm: 560 }, p: 3 }} role="region" aria-label="Incident detail">
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
          <Typography variant="h6" component="h2">
            {query.data?.operation ?? 'Incident'}
          </Typography>
          <IconButton aria-label="Close incident detail" onClick={onClose}>
            <CloseIcon />
          </IconButton>
        </Stack>
        {query.isLoading && <CircularProgress size={24} aria-label="Loading incident" />}
        {query.isError && (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={() => void query.refetch()}>
                Retry
              </Button>
            }
          >
            Could not load this incident.
          </Alert>
        )}
        {query.data && <IncidentBody incident={query.data} onOpenIncident={onOpenIncident} />}
      </Box>
    </Drawer>
  )
}
