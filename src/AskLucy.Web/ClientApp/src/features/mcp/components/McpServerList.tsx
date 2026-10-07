import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  Paper,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
} from '@mui/material'
import DeleteIcon from '@mui/icons-material/Delete'
import EditIcon from '@mui/icons-material/Edit'
import RefreshIcon from '@mui/icons-material/Refresh'
import NetworkCheckIcon from '@mui/icons-material/NetworkCheck'
import KeyIcon from '@mui/icons-material/Key'
import { useT } from '../../../i18n/useT'
import { AdminSectionActions } from '../../admin/components/AdminSectionActions'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import type { McpServer, RegisterMcpServerInput } from '../api/mcpServersApi'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import { useMcpServers } from '../hooks/useMcpServers'
import {
  useDeleteMcpServer,
  useDisableMcpServer,
  useEnableMcpServer,
  useRefreshMcpCapabilities,
  useRegisterMcpServer,
  useRotateMcpServerCredential,
  useTestMcpServerConnection,
  useUpdateMcpServer,
} from '../hooks/useMcpServerMutations'
import { McpServerForm } from './McpServerForm'

interface McpServerListProps {
  selectedServerId: string | null
  onSelectServer: (id: string) => void
  /**
   * Fill the page rather than shrink-wrapping the rows, so the registry card looks the same
   * height whether it holds twenty servers or none. Switched off once a server is selected,
   * where the detail panels below need the room more than an empty gap does.
   */
  fillHeight?: boolean
}

/** spec.md User Story 1 — MCP server registry administration (register/edit/enable/disable/remove/test/refresh). */
export function McpServerList({ selectedServerId, onSelectServer, fillHeight = false }: McpServerListProps) {
  const t = useT('admin.mcpServers')
  const tc = useT('common')
  const { data: servers, isLoading } = useMcpServers()

  const [formOpen, setFormOpen] = useState(false)
  const [editingServer, setEditingServer] = useState<McpServer | undefined>(undefined)
  const [rotatingServer, setRotatingServer] = useState<McpServer | undefined>(undefined)
  const [newCredential, setNewCredential] = useState('')
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const registerServer = useRegisterMcpServer()
  const updateServer = useUpdateMcpServer()
  const deleteServer = useDeleteMcpServer()
  const enableServer = useEnableMcpServer()
  const disableServer = useDisableMcpServer()
  const testConnection = useTestMcpServerConnection()
  const refreshCapabilities = useRefreshMcpCapabilities()
  const rotateCredential = useRotateMcpServerCredential()

  // A transport the catalog does not know (a newer server) is shown as returned.
  const transportLabel = (transport: string) =>
    transport === 'StreamableHttp' || transport === 'Stdio' ? t(`transports.${transport}`) : transport

  const onMutationError = (fallback: string) => (err: unknown) =>
    setErrorMessage(err instanceof Error ? err.message : fallback)

  const openRegisterForm = () => {
    setEditingServer(undefined)
    setErrorMessage(null)
    setFormOpen(true)
  }

  const openEditForm = (server: McpServer) => {
    setEditingServer(server)
    setErrorMessage(null)
    setFormOpen(true)
  }

  const openRotateCredentialDialog = (server: McpServer) => {
    setRotatingServer(server)
    setNewCredential('')
    setErrorMessage(null)
  }

  const handleRotateCredential = () => {
    if (!rotatingServer) return
    rotateCredential.mutate(
      { id: rotatingServer.id, credential: newCredential },
      { onSuccess: () => setRotatingServer(undefined), onError: onMutationError(t('list.errors.rotate')) },
    )
  }

  const handleSubmit = (input: RegisterMcpServerInput) => {
    const onError = onMutationError(t('list.errors.save'))

    if (editingServer) {
      const { name, description, endpoint, transport, authenticationType, requiresUnauthenticatedConfirmation,
        allowInsecureTransport, insecureTransportJustification, endpointValidationOverride,
        endpointValidationJustification, capabilityRefreshIntervalMinutes } = input
      updateServer.mutate(
        {
          id: editingServer.id,
          input: {
            name, description, endpoint, transport, authenticationType, requiresUnauthenticatedConfirmation,
            allowInsecureTransport, insecureTransportJustification, endpointValidationOverride,
            endpointValidationJustification, capabilityRefreshIntervalMinutes,
          },
        },
        { onSuccess: () => setFormOpen(false), onError },
      )
    } else {
      registerServer.mutate(input, { onSuccess: () => setFormOpen(false), onError })
    }
  }

  const isSaving = registerServer.isPending || updateServer.isPending

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header. Not while loading: the skeleton rows
  // fill the body themselves, and stretching would smear six of them over the page.
  const showsStatusRow = !isLoading && (servers?.items ?? []).length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll(fillHeight ? undefined : 480)

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: 0, ...(fillHeight && { flex: 1 }) }}>
      <AdminSectionActions>
        <Button variant="contained" onClick={openRegisterForm}>
          {t('list.register')}
        </Button>
      </AdminSectionActions>

      {errorMessage && (
        <Alert severity="error" sx={{ mb: 2 }} closeText={tc('actions.close')} onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      )}

      <TableContainer
        ref={tableRef}
        component={Paper}
        sx={
          fillHeight
            ? { flex: 1, minHeight: 0, overflow: 'auto', maxHeight: tableMaxHeight }
            : { maxHeight: tableMaxHeight ?? 480, overflow: 'auto' }
        }
      >
        <Table sx={{ '& tr:last-child td': { border: 0 }, height: showsStatusRow ? '100%' : undefined }}>
          <TableHead>
            <TableRow>
              <TableCell>{t('list.columns.name')}</TableCell>
              <TableCell>{t('list.columns.endpoint')}</TableCell>
              <TableCell>{t('list.columns.transport')}</TableCell>
              <TableCell>{t('list.columns.enabled')}</TableCell>
              <TableCell sx={{ textAlign: 'end' }}>{t('list.columns.actions')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={5} />}
            {!isLoading && (servers?.items ?? []).length === 0 && (
              <TableEmptyRow colSpan={5} message={t('list.empty')} />
            )}
            {(servers?.items ?? []).map((server) => (
              <TableRow
                key={server.id}
                hover
                selected={server.id === selectedServerId}
                onClick={() => onSelectServer(server.id)}
                sx={{ cursor: 'pointer' }}
              >
                <TableCell>{server.name}</TableCell>
                <TableCell>
                  <Chip label={<bdi dir="ltr">{server.endpoint}</bdi>} size="small" />
                </TableCell>
                <TableCell>{transportLabel(server.transport)}</TableCell>
                <TableCell>
                  <Switch
                    checked={server.isEnabled}
                    slotProps={{
                      input: {
                        'aria-label': t(server.isEnabled ? 'list.disableAria' : 'list.enableAria', { name: server.name }),
                      },
                    }}
                    onClick={(e) => e.stopPropagation()}
                    onChange={() =>
                      (server.isEnabled ? disableServer : enableServer).mutate(server.id, {
                        onError: onMutationError(t(server.isEnabled ? 'list.errors.disable' : 'list.errors.enable')),
                      })
                    }
                    disabled={enableServer.isPending || disableServer.isPending}
                  />
                </TableCell>
                <TableCell sx={{ textAlign: 'end' }} onClick={(e) => e.stopPropagation()}>
                  <Tooltip title={t('list.testTooltip')}>
                    <IconButton
                      aria-label={t('list.testAria', { name: server.name })}
                      onClick={() => testConnection.mutate(server.id, { onError: onMutationError(t('list.errors.test')) })}
                      disabled={testConnection.isPending}
                    >
                      <NetworkCheckIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title={t('list.refreshTooltip')}>
                    <IconButton
                      aria-label={t('list.refreshAria', { name: server.name })}
                      onClick={() => refreshCapabilities.mutate(server.id, { onError: onMutationError(t('list.errors.refresh')) })}
                      disabled={refreshCapabilities.isPending}
                    >
                      <RefreshIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title={t('list.rotateTooltip')}>
                    <IconButton aria-label={t('list.rotateAria', { name: server.name })} onClick={() => openRotateCredentialDialog(server)}>
                      <KeyIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <IconButton aria-label={t('list.editAria', { name: server.name })} onClick={() => openEditForm(server)}>
                    <EditIcon fontSize="small" />
                  </IconButton>
                  <IconButton
                    aria-label={t('list.deleteAria', { name: server.name })}
                    onClick={() => deleteServer.mutate(server.id, { onError: onMutationError(t('list.errors.delete')) })}
                    disabled={deleteServer.isPending}
                  >
                    <DeleteIcon fontSize="small" />
                  </IconButton>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      <McpServerForm
        open={formOpen}
        server={editingServer}
        isSaving={isSaving}
        errorMessage={errorMessage}
        onClose={() => setFormOpen(false)}
        onSubmit={handleSubmit}
      />

      <Dialog open={rotatingServer !== undefined} onClose={() => setRotatingServer(undefined)} maxWidth="sm" fullWidth>
        <DialogTitle>
          {rotatingServer ? t('list.rotateDialog.titleFor', { name: rotatingServer.name }) : t('list.rotateDialog.title')}
        </DialogTitle>
        <DialogContent>
          <TextField
            label={t('list.rotateDialog.newCredential')}
            type="password"
            fullWidth
            autoFocus
            required
            value={newCredential}
            onChange={(e) => setNewCredential(e.target.value)}
            helperText={t('list.rotateDialog.help')}
            sx={{ mt: 1 }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRotatingServer(undefined)}>{t('list.rotateDialog.cancel')}</Button>
          <Button variant="contained" disabled={!newCredential || rotateCredential.isPending} onClick={handleRotateCredential}>
            {t('list.rotateDialog.rotate')}
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}
