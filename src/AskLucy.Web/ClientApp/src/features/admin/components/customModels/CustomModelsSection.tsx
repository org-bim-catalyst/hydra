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
import { useT } from '../../../../i18n/useT'
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
const FAILURE_KINDS = [
  'SourceNotFound',
  'SourceUnavailable',
  'SourceGatedOrPrivate',
  'SizeLimitExceeded',
  'UnsafeRepositoryPath',
  'ReservedFileName',
  'IntegrityMismatch',
  'DownloadStalled',
  'DiskSpaceExhausted',
  'TargetNotConfigured',
  'TargetAuthRejected',
  'TargetTlsNotAccepted',
  'TargetCertificateInvalid',
  'TargetConnectionLost',
  'TargetWriteRejected',
  'TargetSizeMismatch',
  'InterruptedByRestart',
  'Unexpected',
] as const

const isFailureKind = (kind: string): kind is (typeof FAILURE_KINDS)[number] =>
  (FAILURE_KINDS as readonly string[]).includes(kind)

/** specs/072 — Hugging Face repositories this server has deployed to the production host. */
export function CustomModelsSection() {
  const t = useT('admin.aiProviders')
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
            {t('customModels.title')}
          </Typography>
          {statusQuery.data?.transport === 'FTP' && (
            <Chip
              size="small"
              color="warning"
              variant="outlined"
              label={t('customModels.plainFtp')}
              title={t('customModels.plainFtpTooltip')}
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
            {t('customModels.addModel')}
          </Button>
        )}
      </Box>
      {notConfigured && (
        <Alert severity="info" sx={{ mx: 2, mb: 2 }}>
          {t('customModels.notConfigured')}
        </Alert>
      )}
      {statusQuery.isError && (
        <Alert
          severity="error"
          sx={{ mx: 2, mb: 2 }}
          action={
            <Button color="inherit" size="small" onClick={() => void statusQuery.refetch()}>
              {t('shared.retry')}
            </Button>
          }
        >
          {errorMessage(statusQuery.error, t)}
        </Alert>
      )}
      {connectionLost && (
        <Alert severity="warning" sx={{ mx: 2, mb: 2 }}>
          {t('customModels.liveUpdatesLost')}
        </Alert>
      )}
      {listQuery.isError && (
        <Alert
          severity="error"
          sx={{ mx: 2, mb: 2 }}
          action={
            <Button color="inherit" size="small" onClick={() => void listQuery.refetch()}>
              {t('shared.retry')}
            </Button>
          }
        >
          {errorMessage(listQuery.error, t)}
        </Alert>
      )}
      <TableContainer sx={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
        <Table size="small" stickyHeader aria-labelledby="custom-models-heading">
          <TableHead>
            <TableRow>
              <TableCell>{t('customModels.columns.name')}</TableCell>
              <TableCell>{t('customModels.columns.source')}</TableCell>
              <TableCell>{t('customModels.columns.destination')}</TableCell>
              <TableCell>{t('customModels.columns.size')}</TableCell>
              <TableCell>{t('customModels.columns.state')}</TableCell>
              <TableCell>{t('customModels.columns.progress')}</TableCell>
              <TableCell>{t('customModels.columns.available')}</TableCell>
              <TableCell>{t('shared.actions')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {listQuery.isLoading && <TableLoadingRow colSpan={COLUMNS} rows={3} />}
            {listQuery.isSuccess && models.length === 0 && (
              <TableEmptyRow colSpan={COLUMNS} message={t('customModels.empty')} />
            )}
            {models.map((model) => (
              <TableRow key={model.id}>
                <TableCell>
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                    <bdi>{model.name}</bdi>
                    {model.selectedForLocalWhisper && (
                      <Chip size="small" color="primary" label={t('customModels.localWhisper')} />
                    )}
                  </Stack>
                </TableCell>
                <TableCell>
                  <bdi dir="ltr">
                    {model.repositoryId}@{model.revision}
                  </bdi>
                </TableCell>
                <TableCell>
                  <bdi dir="ltr">{model.destination}</bdi>
                </TableCell>
                <TableCell>
                  {model.totalBytes === null ? (
                    '—'
                  ) : (
                    <bdi dir="ltr">{formatBytes(model.totalBytes, t('shared.bytes'))}</bdi>
                  )}
                </TableCell>
                <TableCell>
                  <Stack spacing={0.5} sx={{ alignItems: 'flex-start' }}>
                    <Chip
                      size="small"
                      label={t(`customModels.state.${model.deploymentState}`)}
                      color={STATE_COLOR[model.deploymentState]}
                    />
                    {model.deploymentState === 'Failed' && model.failureKind && (
                      <Chip
                        size="small"
                        variant="outlined"
                        color="error"
                        label={
                          isFailureKind(model.failureKind)
                            ? t(`customModels.failureKind.${model.failureKind}`)
                            : model.failureKind
                        }
                      />
                    )}
                    {model.deploymentState === 'Failed' && model.failureReason && (
                      <Typography variant="body2" color="error">
                        {model.failureReason}
                      </Typography>
                    )}
                    {model.overwrittenFileCount > 0 && (
                      <Link
                        component="button"
                        variant="caption"
                        onClick={() => setOverwrittenFor(model)}
                      >
                        {t('customModels.filesOverwritten', { count: model.overwrittenFileCount })}
                      </Link>
                    )}
                  </Stack>
                </TableCell>
                <TableCell>
                  {IN_PROGRESS.includes(model.deploymentState) && (
                    <CustomModelProgress
                      model={model}
                      canManage={canManage}
                      phase={phaseById[model.id]}
                    />
                  )}
                </TableCell>
                <TableCell>
                  {canManage ? (
                    <CustomModelAvailabilitySwitch model={model} />
                  ) : (
                    t(`customModels.availability.${model.availability}`)
                  )}
                </TableCell>
                <TableCell>
                  {canManage && (model.canRemove || model.selectedForLocalWhisper) && (
                    <RemoveCustomModelButton model={model} />
                  )}
                </TableCell>
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
