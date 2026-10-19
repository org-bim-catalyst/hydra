import {
  Alert,
  Button,
  CircularProgress,
  Paper,
  Snackbar,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError } from '../../../api/httpClient'
import { useIsAdmin } from '../../../hooks/useIsAdmin'
import { useCan } from '../../auth/hooks/usePermissions'
import { AdminShell } from '../components/AdminShell'
import {
  getProviderHealthRetention,
  PROVIDER_HEALTH_RETENTION_QUERY_KEY,
  updateProviderHealthRetention,
  type ProviderHealthRetention,
} from './providerHealthRetentionApi'

const MIN_DAYS = 1
const MAX_DAYS = 365

function errorMessage(error: unknown, fallback: string): string {
  if (!(error instanceof ApiError)) {
    return fallback
  }
  return error.errors?.RetentionDays?.[0] ?? error.detail ?? error.message ?? fallback
}

/** specs/082 — the platform-wide window is readable by any operational-failures viewer and editable by a manager. */
export function AdminProviderHealthRetentionPage() {
  return (
    <AdminShell
      title="Provider health checks"
      subtitle="How long provider health-check history is kept."
    >
      <RetentionContent />
    </AdminShell>
  )
}

function RetentionContent() {
  const isAdmin = useIsAdmin()
  const hasManagePermission = useCan('admin.operational-failures.manage')
  const canManage = isAdmin || hasManagePermission
  const queryClient = useQueryClient()
  const [saved, setSaved] = useState(false)
  const query = useQuery({
    queryKey: PROVIDER_HEALTH_RETENTION_QUERY_KEY,
    queryFn: getProviderHealthRetention,
  })
  const update = useMutation({
    mutationFn: (input: { rowVersion: string; retentionDays: number }) =>
      updateProviderHealthRetention(input.rowVersion, input.retentionDays),
    onSuccess: (data) => {
      queryClient.setQueryData(PROVIDER_HEALTH_RETENTION_QUERY_KEY, data)
      setSaved(true)
    },
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
          <Button color="inherit" size="small" onClick={() => void query.refetch()}>
            Retry
          </Button>
        }
      >
        {message}
      </Alert>
    )
  }

  const conflict = update.error instanceof ApiError && update.error.status === 409

  const reload = () => {
    update.reset()
    void query.refetch()
  }

  return (
    <>
      <Stack spacing={2}>
        {update.isError && (
          <Alert
            severity="error"
            action={
              conflict ? (
                <Button color="inherit" size="small" onClick={reload}>
                  Reload
                </Button>
              ) : undefined
            }
          >
            {conflict
              ? errorMessage(update.error, 'The retention window was changed by someone else. Reload it and try again.')
              : errorMessage(update.error, 'The retention window could not be saved.')}
          </Alert>
        )}

        <RetentionForm
          key={query.data.rowVersion ?? query.data.source}
          data={query.data}
          canManage={canManage}
          isSaving={update.isPending}
          onSave={(retentionDays) => {
            setSaved(false)
            if (query.data.rowVersion) {
              update.mutate({ rowVersion: query.data.rowVersion, retentionDays })
            }
          }}
        />
      </Stack>

      <Snackbar
        open={saved}
        autoHideDuration={5000}
        onClose={() => setSaved(false)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity="success" variant="filled" onClose={() => setSaved(false)}>
          Retention window saved.
        </Alert>
      </Snackbar>
    </>
  )
}

interface RetentionFormProps {
  data: ProviderHealthRetention
  canManage: boolean
  isSaving: boolean
  onSave: (retentionDays: number) => void
}

function RetentionForm({ data, canManage, isSaving, onSave }: RetentionFormProps) {
  const [value, setValue] = useState(String(data.retentionDays))
  const parsed = Number(value)
  const valid = Number.isInteger(parsed) && parsed >= MIN_DAYS && parsed <= MAX_DAYS
  const dirty = value !== String(data.retentionDays)
  const editable = canManage && data.rowVersion !== null

  return (
    <Paper elevation={1} sx={{ p: 2 }}>
      <Stack spacing={2}>
        {data.source === 'Configuration' && (
          <Alert severity="warning">
            The stored setting is missing, so the configured fallback of {data.retentionDays} days is in use. Restore
            the setting row before changing it.
          </Alert>
        )}

        <TextField
          label="Days kept"
          type="number"
          value={value}
          onChange={(event) => setValue(event.target.value)}
          disabled={!editable || isSaving}
          error={value.trim() !== '' && !valid}
          helperText={
            value.trim() !== '' && !valid
              ? `Enter a whole number from ${MIN_DAYS} to ${MAX_DAYS}.`
              : `Keep between ${MIN_DAYS} and ${MAX_DAYS} days of history.`
          }
          slotProps={{ htmlInput: { min: MIN_DAYS, max: MAX_DAYS, step: 1 } }}
          sx={{ maxWidth: 260 }}
        />

        <Typography variant="body2" color="text.secondary">
          Checks run every {data.intervalMinutes} minutes. A daily job at 03:30 removes older rows.
        </Typography>

        {editable ? (
          <Stack direction="row">
            <Button variant="contained" disabled={!dirty || !valid || isSaving} onClick={() => onSave(parsed)}>
              Save
            </Button>
          </Stack>
        ) : (
          !canManage && (
            <Typography variant="body2" color="text.secondary">
              You can view this value but not change it.
            </Typography>
          )
        )}
      </Stack>
    </Paper>
  )
}
