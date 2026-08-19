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
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography,
} from '@mui/material'
import DeleteIcon from '@mui/icons-material/Delete'
import EditIcon from '@mui/icons-material/Edit'
import RefreshIcon from '@mui/icons-material/Refresh'
import NetworkCheckIcon from '@mui/icons-material/NetworkCheck'
import KeyIcon from '@mui/icons-material/Key'
import type { McpServer, RegisterMcpServerInput } from '../api/mcpServersApi'
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
}

/** spec.md User Story 1 — MCP server registry administration (register/edit/enable/disable/remove/test/refresh). */
export function McpServerList({ selectedServerId, onSelectServer }: McpServerListProps) {
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
      { onSuccess: () => setRotatingServer(undefined), onError: onMutationError('Could not rotate the credential. Please try again.') },
    )
  }

  const handleSubmit = (input: RegisterMcpServerInput) => {
    const onError = onMutationError('Could not save the server. Please try again.')

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

  return (
    <Box>
      <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
        <Typography variant="h6">MCP Servers</Typography>
        <Button variant="contained" onClick={openRegisterForm}>
          Register server
        </Button>
      </Stack>

      {errorMessage && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      )}

      <TableContainer component={Paper}>
        <Table>
          <TableHead>
            <TableRow>
              <TableCell>Name</TableCell>
              <TableCell>Endpoint</TableCell>
              <TableCell>Transport</TableCell>
              <TableCell>Enabled</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && (
              <TableRow>
                <TableCell colSpan={5}>Loading…</TableCell>
              </TableRow>
            )}
            {!isLoading && (servers?.items ?? []).length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>No MCP servers registered yet.</TableCell>
              </TableRow>
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
                  <Chip label={server.endpoint} size="small" />
                </TableCell>
                <TableCell>{server.transport}</TableCell>
                <TableCell>
                  <Switch
                    checked={server.isEnabled}
                    slotProps={{ input: { 'aria-label': `${server.isEnabled ? 'Disable' : 'Enable'} ${server.name}` } }}
                    onClick={(e) => e.stopPropagation()}
                    onChange={() =>
                      (server.isEnabled ? disableServer : enableServer).mutate(server.id, {
                        onError: onMutationError(`Could not ${server.isEnabled ? 'disable' : 'enable'} the server. Please try again.`),
                      })
                    }
                    disabled={enableServer.isPending || disableServer.isPending}
                  />
                </TableCell>
                <TableCell align="right" onClick={(e) => e.stopPropagation()}>
                  <Tooltip title="Test connection">
                    <IconButton
                      aria-label={`Test connection to ${server.name}`}
                      onClick={() => testConnection.mutate(server.id, { onError: onMutationError('Could not test the connection. Please try again.') })}
                      disabled={testConnection.isPending}
                    >
                      <NetworkCheckIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title="Refresh capabilities">
                    <IconButton
                      aria-label={`Refresh capabilities for ${server.name}`}
                      onClick={() => refreshCapabilities.mutate(server.id, { onError: onMutationError('Could not refresh capabilities. Please try again.') })}
                      disabled={refreshCapabilities.isPending}
                    >
                      <RefreshIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title="Rotate credential">
                    <IconButton aria-label={`Rotate credential for ${server.name}`} onClick={() => openRotateCredentialDialog(server)}>
                      <KeyIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                  <IconButton aria-label={`Edit ${server.name}`} onClick={() => openEditForm(server)}>
                    <EditIcon fontSize="small" />
                  </IconButton>
                  <IconButton
                    aria-label={`Delete ${server.name}`}
                    onClick={() => deleteServer.mutate(server.id, { onError: onMutationError('Could not delete the server. It may still be referenced by an agent tool.') })}
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
        <DialogTitle>Rotate credential{rotatingServer ? ` for ${rotatingServer.name}` : ''}</DialogTitle>
        <DialogContent>
          <TextField
            label="New credential"
            type="password"
            fullWidth
            autoFocus
            required
            value={newCredential}
            onChange={(e) => setNewCredential(e.target.value)}
            helperText="The existing credential is never displayed. In-flight calls on the old connection complete or fail independently; new calls use this value immediately."
            sx={{ mt: 1 }}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRotatingServer(undefined)}>Cancel</Button>
          <Button variant="contained" disabled={!newCredential || rotateCredential.isPending} onClick={handleRotateCredential}>
            Rotate
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  )
}
