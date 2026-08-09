import { Box, List, ListItem, ListItemText, Paper, Stack, Typography } from '@mui/material'
import type { DocumentDashboardSummary } from '../api/documentsApi'
import { ErrorState } from '../../../components/ErrorState'
import { SkeletonBlock } from '../../../components/SkeletonBlock'

function StatTile({ label, value, emphasizeAsError }: { label: string; value: number; emphasizeAsError?: boolean }) {
  return (
    <Paper variant="outlined" sx={{ p: 1.5, minWidth: 120, flex: '1 1 auto' }}>
      <Typography variant="h5" color={emphasizeAsError && value > 0 ? 'error.main' : 'text.primary'}>
        {value}
      </Typography>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
    </Paper>
  )
}

/** Shared by both `ProcessingDashboard` (per-user) and `OrganizationDashboard` (admin-only) — they render the identical shape (contracts/document-processing-api.md), scoped differently by the query each calls. */
export function DashboardBody({ data }: { data: DocumentDashboardSummary }) {
  return (
    <Box>
      <Stack direction="row" spacing={1.5} sx={{ flexWrap: 'wrap', mb: 2 }}>
        <StatTile label="Queued" value={data.queueDepth} />
        <StatTile label="In progress" value={data.inProgressCount} />
        <StatTile label="Completed today" value={data.completedTodayCount} />
        <StatTile label="Failed" value={data.failedCount} emphasizeAsError />
      </Stack>

      <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
        Storage
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
        {(data.statistics.totalStorageBytes / (1024 * 1024)).toFixed(1)} MB across {data.statistics.totalDocuments} document
        {data.statistics.totalDocuments === 1 ? '' : 's'}
        {data.statistics.averageProcessingDurationMs !== null &&
          ` · ${(data.statistics.averageProcessingDurationMs / 1000).toFixed(1)}s average processing time`}
      </Typography>

      {data.retryQueue.length > 0 && (
        <>
          <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
            Retry queue
          </Typography>
          <List dense>
            {data.retryQueue.map((entry) => (
              <ListItem key={entry.documentId} disableGutters>
                <ListItemText primary={entry.fileName} secondary={entry.failureReason} />
              </ListItem>
            ))}
          </List>
        </>
      )}
    </Box>
  )
}

interface DashboardProps {
  data?: DocumentDashboardSummary
  isLoading: boolean
  isError: boolean
}

/** FR-045, US6 AC1 — per-user processing dashboard; live counts poll every 5s (research.md Decision 7's reconciliation pattern). */
export function ProcessingDashboard({ data, isLoading, isError }: DashboardProps) {
  if (isError) {
    // Polls every 5s (research.md Decision 7) — no manual retry action needed, it self-heals.
    return <ErrorState title="Could not load your dashboard" description="Retrying automatically…" />
  }

  if (isLoading || !data) {
    return <SkeletonBlock variant="card" count={4} />
  }

  return <DashboardBody data={data} />
}
