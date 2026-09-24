import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  Link,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import type { ChipProps } from '@mui/material'
import AddIcon from '@mui/icons-material/Add'
import { useQuery } from '@tanstack/react-query'
import { TableEmptyRow } from '../../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../../components/TableLoadingRow'
import { useIsAdmin } from '../../../../hooks/useIsAdmin'
import { useCan } from '../../../auth/hooks/usePermissions'
import { useCustomModelDeploymentsHub } from '../../hooks/useCustomModelDeploymentsHub'
import * as customModelsApi from '../../api/adminCustomModelsApi'
import type { CustomModelSummary, DeploymentState } from '../../api/adminCustomModelsApi'
import { AddCustomModelDialog } from './AddCustomModelDialog'
import { CustomModelAvailabilitySwitch } from './CustomModelAvailabilitySwitch'
import { CustomModelProgress } from './CustomModelProgress'
import { formatBytes } from './formatBytes'
import { errorMessage } from './errorMessage'
import { OverwrittenFilesDialog } from './OverwrittenFilesDialog'
import { RemoveCustomModelButton } from './RemoveCustomModelButton'

const PAGE_SIZE = 50
const COLUMNS = 8

const IN_PROGRESS: DeploymentState[] = ['Queued', 'Listing', 'Transferring']

const STATE_COLOR: Record<DeploymentState, ChipProps['color']> = {
  Queued: 'default',
  Listing: 'info',
  Transferring: 'info',
  Completed: 'success',
  Failed: 'error',
  Cancelled: 'warning',
}

// Mirrors the domain's CustomModelFailureKind; a kind added server-side first is shown as it is.
const FAILURE_KIND_LABEL: Record<string, string> = {
  SourceNotFound: 'Source not found',
  SourceUnavailable: 'Source unavailable',
  SourceGatedOrPrivate: 'Gated or private',
  SizeLimitExceeded: 'Too large',
  UnsafeRepositoryPath: 'Unsafe file path',
  ReservedFileName: 'Reserved file name',
  IntegrityMismatch: 'Integrity mismatch',
  DownloadStalled: 'Download stalled',
  DiskSpaceExhausted: 'Out of disk space',
  TargetNotConfigured: 'Not configured',
  TargetAuthRejected: 'Sign-in rejected',
  TargetTlsNotAccepted: 'TLS not accepted',
  TargetCertificateInvalid: 'Invalid certificate',
  TargetConnectionLost: 'Connection lost',
  TargetWriteRejected: 'Write rejected',
  TargetSizeMismatch: 'Size mismatch',
  InterruptedByRestart: 'Interrupted by restart',
  Unexpected: 'Unexpected error',
}

/** specs/072 — Hugging Face repositories this server has deployed to the production host. */
export function CustomModelsSection() {
  const isAdmin = useIsAdmin()
  const canManage = useCan('admin.custom-models.manage') || isAdmin
  const [addOpen, setAddOpen] = useState(false)
  const [overwrittenFor, setOverwrittenFor] = useState<CustomModelSummary | null>(null)
  const { connectionLost, phaseById } = useCustomModelDeploymentsHub()

  const listQuery = useQuery({
    queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.list(1, PAGE_SIZE),
    queryFn: () => customModelsApi.getCustomModels(1, PAGE_SIZE),
  })
  const models = listQuery.data?.items ?? []

  const statusQuery = useQuery({
    queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.deploymentStatus,
    queryFn: customModelsApi.getDeploymentStatus,
  })
  const notConfigured = statusQuery.data?.isConfigured === false

  return (
    <Paper
      variant="outlined"
      sx={{ mt: 3, flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}
      component="section"
      aria-labelledby="custom-models-heading"
    >
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', p: 2 }}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <Typography id="custom-models-heading" variant="subtitle1" component="h2">
            Custom models
          </Typography>
          {statusQuery.data?.transport === 'FTP' && (
            <Chip
              size="small"
              color="warning"
              variant="outlined"
              label="Plain FTP"
              title="This server deploys over plain FTP, so files and credentials travel unencrypted."
            />
          )}
        </Stack>
        {canManage && (
          <Button
            size="small"
            variant="outlined"
            startIcon={<AddIcon fontSize="small" />}
            onClick={() => setAddOpen(true)}
            disabled={notConfigured}
            sx={{
              color: 'text.primary',
              borderColor: 'divider',
              '&:hover': { borderColor: 'text.secondary', bgcolor: 'action.hover' },
            }}
          >
            Add model
          </Button>
        )}
      </Box>
      {notConfigured && (
        <Alert severity="info" sx={{ mx: 2, mb: 2 }}>
          Deployment not configured. Ask whoever runs this server to set up the deployment target first.
        </Alert>
      )}
      {statusQuery.isError && (
        <Alert
          severity="error"
          sx={{ mx: 2, mb: 2 }}
          action={
            <Button color="inherit" size="small" onClick={() => void statusQuery.refetch()}>
              Retry
            </Button>
          }
        >
          {errorMessage(statusQuery.error)}
        </Alert>
      )}
      {connectionLost && (
        <Alert severity="warning" sx={{ mx: 2, mb: 2 }}>
          Live updates disconnected — reconnecting…
        </Alert>
      )}
      {listQuery.isError && (
        <Alert
          severity="error"
          sx={{ mx: 2, mb: 2 }}
          action={
            <Button color="inherit" size="small" onClick={() => void listQuery.refetch()}>
              Retry
            </Button>
          }
        >
          {errorMessage(listQuery.error)}
        </Alert>
      )}
      <TableContainer sx={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
        <Table size="small" stickyHeader aria-labelledby="custom-models-heading">
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Source</TableCell>
              <TableCell>Destination</TableCell>
              <TableCell>Size</TableCell>
              <TableCell>State</TableCell>
              <TableCell>Progress</TableCell>
              <TableCell>Available</TableCell>
              <TableCell>Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {listQuery.isLoading && <TableLoadingRow colSpan={COLUMNS} rows={3} />}
            {listQuery.isSuccess && models.length === 0 && (
              <TableEmptyRow colSpan={COLUMNS} message="No custom models yet." />
            )}
            {models.map((model) => (
              <TableRow key={model.id}>
                <TableCell>
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <span>{model.name}</span>
                    {/* specs/072 — the on-server engine this model's repository feeds. */}
                    {model.backsEngine && <Chip size="small" variant="outlined" label={`Backs ${model.backsEngine}`} />}
                  </Stack>
                </TableCell>
                <TableCell>
                  {model.repositoryId}@{model.revision}
                </TableCell>
                <TableCell>{model.destination}</TableCell>
                <TableCell>{model.totalBytes === null ? '—' : formatBytes(model.totalBytes)}</TableCell>
                <TableCell>
                  <Stack spacing={0.5} sx={{ alignItems: 'flex-start' }}>
                    <Chip size="small" label={model.deploymentState} color={STATE_COLOR[model.deploymentState]} />
                    {model.deploymentState === 'Failed' && model.failureKind && (
                      <Chip
                        size="small"
                        variant="outlined"
                        color="error"
                        label={FAILURE_KIND_LABEL[model.failureKind] ?? model.failureKind}
                      />
                    )}
                    {model.deploymentState === 'Failed' && model.failureReason && (
                      <Typography variant="body2" color="error">
                        {model.failureReason}
                      </Typography>
                    )}
                    {model.overwrittenFileCount > 0 && (
                      <Link component="button" variant="caption" onClick={() => setOverwrittenFor(model)}>
                        {model.overwrittenFileCount === 1
                          ? '1 file overwritten'
                          : `${model.overwrittenFileCount} files overwritten`}
                      </Link>
                    )}
                  </Stack>
                </TableCell>
                <TableCell>
                  {IN_PROGRESS.includes(model.deploymentState) && (
                    <CustomModelProgress model={model} canManage={canManage} phase={phaseById[model.id]} />
                  )}
                </TableCell>
                <TableCell>
                  {canManage ? <CustomModelAvailabilitySwitch model={model} /> : model.availability}
                </TableCell>
                <TableCell>{canManage && model.canRemove && <RemoveCustomModelButton model={model} />}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {canManage && <AddCustomModelDialog open={addOpen} onClose={() => setAddOpen(false)} />}
      {overwrittenFor && (
        <OverwrittenFilesDialog
          open
          modelId={overwrittenFor.id}
          modelName={overwrittenFor.name}
          onClose={() => setOverwrittenFor(null)}
        />
      )}
    </Paper>
  )
}
