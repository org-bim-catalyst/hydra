import { useState } from 'react'
import {
  Alert,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  MenuItem,
  Stack,
  TextField,
} from '@mui/material'
import type { McpAuthenticationType, McpServer, McpServerTransport, RegisterMcpServerInput } from '../api/mcpServersApi'

const TRANSPORTS: McpServerTransport[] = ['StreamableHttp', 'Stdio']
const AUTH_TYPES: McpAuthenticationType[] = ['None', 'ApiKey', 'BearerToken', 'OAuth2ClientCredentials']

interface McpServerFormProps {
  open: boolean
  /** Present when editing an existing server — its `credential` field is never pre-filled or echoed back (FR-045/FR-046). */
  server?: McpServer
  isSaving: boolean
  errorMessage: string | null
  onClose: () => void
  onSubmit: (input: RegisterMcpServerInput) => void
}

/** spec.md FR-001-FR-010 — register/edit an MCP server. */
export function McpServerForm({ open, server, isSaving, errorMessage, onClose, onSubmit }: McpServerFormProps) {
  const isEdit = server !== undefined

  const [name, setName] = useState(server?.name ?? '')
  const [description, setDescription] = useState(server?.description ?? '')
  const [endpoint, setEndpoint] = useState(server?.endpoint ?? '')
  const [transport, setTransport] = useState<McpServerTransport>(server?.transport ?? 'StreamableHttp')
  const [authenticationType, setAuthenticationType] = useState<McpAuthenticationType>(server?.authenticationType ?? 'ApiKey')
  const [credential, setCredential] = useState('')
  const [requiresUnauthenticatedConfirmation, setRequiresUnauthenticatedConfirmation] = useState(
    server?.requiresUnauthenticatedConfirmation ?? false,
  )
  const [allowInsecureTransport, setAllowInsecureTransport] = useState(server?.allowInsecureTransport ?? false)
  const [insecureTransportJustification, setInsecureTransportJustification] = useState(server?.insecureTransportJustification ?? '')
  const [endpointValidationOverride, setEndpointValidationOverride] = useState(server?.endpointValidationOverride ?? false)
  const [endpointValidationJustification, setEndpointValidationJustification] = useState(server?.endpointValidationJustification ?? '')
  const [capabilityRefreshIntervalMinutes, setCapabilityRefreshIntervalMinutes] = useState(
    server?.capabilityRefreshIntervalMinutes ?? 60,
  )

  const isUnauthenticated = authenticationType === 'None'
  const needsInsecureJustification = allowInsecureTransport && !insecureTransportJustification
  const needsEndpointJustification = endpointValidationOverride && !endpointValidationJustification
  const canSubmit = name.trim() !== '' && endpoint.trim() !== '' && !needsInsecureJustification && !needsEndpointJustification

  const handleSubmit = () => {
    onSubmit({
      name,
      description: description || null,
      endpoint,
      transport,
      authenticationType,
      credential: isEdit ? null : credential || null,
      requiresUnauthenticatedConfirmation: isUnauthenticated && requiresUnauthenticatedConfirmation,
      allowInsecureTransport,
      insecureTransportJustification: allowInsecureTransport ? insecureTransportJustification : null,
      endpointValidationOverride,
      endpointValidationJustification: endpointValidationOverride ? endpointValidationJustification : null,
      capabilityRefreshIntervalMinutes,
    })
  }

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{isEdit ? `Edit ${server.name}` : 'Register MCP server'}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="Name" required value={name} onChange={(e) => setName(e.target.value)} />
          <TextField label="Description" multiline minRows={2} value={description} onChange={(e) => setDescription(e.target.value)} />
          <TextField label="Endpoint" required value={endpoint} onChange={(e) => setEndpoint(e.target.value)} helperText="e.g. https://mcp.example.com or a stdio command" />
          <TextField select label="Transport" value={transport} onChange={(e) => setTransport(e.target.value as McpServerTransport)}>
            {TRANSPORTS.map((t) => (
              <MenuItem key={t} value={t}>
                {t}
              </MenuItem>
            ))}
          </TextField>
          <TextField select label="Authentication type" value={authenticationType} onChange={(e) => setAuthenticationType(e.target.value as McpAuthenticationType)}>
            {AUTH_TYPES.map((t) => (
              <MenuItem key={t} value={t}>
                {t}
              </MenuItem>
            ))}
          </TextField>
          {!isEdit && !isUnauthenticated && (
            <TextField
              label="Credential"
              type="password"
              value={credential}
              onChange={(e) => setCredential(e.target.value)}
              helperText="Stored encrypted server-side; never displayed again. Rotate credentials separately."
            />
          )}
          {!isEdit && isUnauthenticated && (
            <FormControlLabel
              control={<Checkbox checked={requiresUnauthenticatedConfirmation} onChange={(e) => setRequiresUnauthenticatedConfirmation(e.target.checked)} />}
              label="I understand this server requires no authentication and explicitly approve it"
            />
          )}
          <FormControlLabel
            control={<Checkbox checked={allowInsecureTransport} onChange={(e) => setAllowInsecureTransport(e.target.checked)} />}
            label="Allow insecure (non-TLS) transport"
          />
          {allowInsecureTransport && (
            <TextField
              label="Insecure transport justification"
              required
              value={insecureTransportJustification}
              onChange={(e) => setInsecureTransportJustification(e.target.value)}
            />
          )}
          <FormControlLabel
            control={<Checkbox checked={endpointValidationOverride} onChange={(e) => setEndpointValidationOverride(e.target.checked)} />}
            label="Override endpoint SSRF validation (private/loopback/link-local)"
          />
          {endpointValidationOverride && (
            <TextField
              label="Endpoint validation override justification"
              required
              value={endpointValidationJustification}
              onChange={(e) => setEndpointValidationJustification(e.target.value)}
            />
          )}
          <TextField
            label="Capability refresh interval (minutes)"
            type="number"
            value={capabilityRefreshIntervalMinutes}
            onChange={(e) => setCapabilityRefreshIntervalMinutes(Number(e.target.value))}
          />
          {errorMessage && <Alert severity="error">{errorMessage}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Cancel</Button>
        <Button variant="contained" disabled={!canSubmit || isSaving} onClick={handleSubmit}>
          {isEdit ? 'Save changes' : 'Register server'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
