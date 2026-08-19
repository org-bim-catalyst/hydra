import { useState } from 'react'
import {
  Alert,
  Box,
  Chip,
  IconButton,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import CheckCircleIcon from '@mui/icons-material/CheckCircle'
import CancelIcon from '@mui/icons-material/Cancel'
import type { AgentToolRiskLevel, McpToolActivationStatus } from '../api/mcpServersApi'
import { useMcpServerTools } from '../hooks/useMcpServers'
import { useActivateMcpTool, useDeactivateMcpTool } from '../hooks/useMcpServerMutations'

const RISK_COLOR: Record<AgentToolRiskLevel, 'success' | 'info' | 'warning' | 'error'> = {
  Low: 'success',
  Medium: 'info',
  High: 'warning',
  Critical: 'error',
}

const STATUS_COLOR: Record<McpToolActivationStatus, 'default' | 'success' | 'warning'> = {
  PendingReview: 'warning',
  Active: 'success',
  Deactivated: 'default',
}

/**
 * spec.md FR-021/FR-022 — the mandatory admin review gate: a newly-discovered (or changed) tool
 * always starts `PendingReview` regardless of what the server itself declares, so an administrator
 * must explicitly activate it before any agent can use it.
 */
export function McpToolActivationPanel({ serverId }: { serverId: string }) {
  const { data: tools, isLoading } = useMcpServerTools(serverId)
  const activateTool = useActivateMcpTool()
  const deactivateTool = useDeactivateMcpTool()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const onMutationError = (fallback: string) => (err: unknown) => setErrorMessage(err instanceof Error ? err.message : fallback)

  return (
    <Box>
      <Typography variant="subtitle1" sx={{ mb: 1 }}>
        Tools
      </Typography>

      {errorMessage && (
        <Alert severity="error" sx={{ mb: 2 }} onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      )}

      <TableContainer component={Paper}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>Tool</TableCell>
              <TableCell>Risk</TableCell>
              <TableCell>Required permissions</TableCell>
              <TableCell>Status</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && (
              <TableRow>
                <TableCell colSpan={5}>Loading…</TableCell>
              </TableRow>
            )}
            {!isLoading && (tools ?? []).length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>No tools discovered yet. Refresh capabilities to discover them.</TableCell>
              </TableRow>
            )}
            {(tools ?? []).map((tool) => (
              <TableRow key={tool.id}>
                <TableCell>
                  <Tooltip title={tool.description}>
                    <span>{tool.displayName}</span>
                  </Tooltip>
                </TableCell>
                <TableCell>
                  <Chip label={tool.effectiveRiskLevel} color={RISK_COLOR[tool.effectiveRiskLevel]} size="small" />
                </TableCell>
                <TableCell>
                  <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap' }}>
                    {tool.requiredPermissions.map((permission) => (
                      <Chip key={permission} label={permission} size="small" variant="outlined" />
                    ))}
                  </Stack>
                </TableCell>
                <TableCell>
                  <Chip label={tool.activationStatus} color={STATUS_COLOR[tool.activationStatus]} size="small" />
                </TableCell>
                <TableCell align="right">
                  {tool.activationStatus !== 'Active' ? (
                    <Tooltip title="Activate">
                      <IconButton
                        aria-label={`Activate ${tool.displayName}`}
                        color="success"
                        disabled={activateTool.isPending}
                        onClick={() =>
                          activateTool.mutate(
                            { serverId, toolId: tool.id, input: { effectiveRiskLevelOverride: null, requiredPermissionsJsonOverride: null } },
                            { onError: onMutationError('Could not activate the tool. Please try again.') },
                          )
                        }
                      >
                        <CheckCircleIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  ) : (
                    <Tooltip title="Deactivate">
                      <IconButton
                        aria-label={`Deactivate ${tool.displayName}`}
                        color="error"
                        disabled={deactivateTool.isPending}
                        onClick={() =>
                          deactivateTool.mutate(
                            { serverId, toolId: tool.id },
                            { onError: onMutationError('Could not deactivate the tool. Please try again.') },
                          )
                        }
                      >
                        <CancelIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  )
}
