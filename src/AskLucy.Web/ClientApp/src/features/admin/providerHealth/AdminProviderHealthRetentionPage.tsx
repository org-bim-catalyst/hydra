import { Alert, Button, CircularProgress, Paper, Stack, Typography } from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import { AdminShell } from '../components/AdminShell'
import { getProviderHealthRetention, PROVIDER_HEALTH_RETENTION_QUERY_KEY } from './providerHealthRetentionApi'

/** Read-only view of the provider health-check retention window. Changing it is a deployment setting for now. */
export function AdminProviderHealthRetentionPage() {
  return (
    <AdminShell
      title="Provider health checks"
      subtitle="How long provider health-check history is kept. Read-only: the value comes from configuration."
    >
      <RetentionContent />
    </AdminShell>
  )
}

function RetentionContent() {
  const query = useQuery({
    queryKey: PROVIDER_HEALTH_RETENTION_QUERY_KEY,
    queryFn: getProviderHealthRetention,
  })

  if (query.isPending) {
    return <CircularProgress aria-label="Loading retention settings" />
  }

  if (query.isError) {
    const message = query.error instanceof ApiError ? query.error.message : 'The retention settings could not be loaded.'
    return (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={() => query.refetch()}>
            Retry
          </Button>
        }
      >
        {message}
      </Alert>
    )
  }

  return (
    <Paper elevation={1} sx={{ p: 2 }}>
      <Stack spacing={1}>
        <Typography variant="body1">
          Health-check history is deleted after <strong>{query.data.retentionDays} days</strong>.
        </Typography>
        <Typography variant="body2" color="text.secondary">
          Checks run every {query.data.intervalMinutes} minutes. A daily job at 03:30 removes older rows.
        </Typography>
      </Stack>
    </Paper>
  )
}
