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
import { useT } from '../../../i18n/useT'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
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
  const t = useT('admin.mcpServers')
  const tc = useT('common')
  const { data: tools, isLoading } = useMcpServerTools(serverId)
  const activateTool = useActivateMcpTool()
  const deactivateTool = useDeactivateMcpTool()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const onMutationError = (fallback: string) => (err: unknown) => setErrorMessage(err instanceof Error ? err.message : fallback)

  return (
    <Box>
      <Typography variant="subtitle1" sx={{ mb: 1 }}>
        {t('tools.title')}
      </Typography>

      {errorMessage && (
        <Alert severity="error" sx={{ mb: 2 }} closeText={tc('actions.close')} onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      )}

      <TableContainer component={Paper}>
        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t('tools.columns.tool')}</TableCell>
              <TableCell>{t('tools.columns.risk')}</TableCell>
              <TableCell>{t('tools.columns.permissions')}</TableCell>
              <TableCell>{t('tools.columns.status')}</TableCell>
              <TableCell sx={{ textAlign: 'end' }}>{t('tools.columns.actions')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {isLoading && <TableLoadingRow colSpan={5} />}
            {!isLoading && (tools ?? []).length === 0 && (
              <TableEmptyRow colSpan={5} message={t('tools.empty')} />
            )}
            {(tools ?? []).map((tool) => (
              <TableRow key={tool.id}>
                <TableCell>
                  <Tooltip title={tool.description}>
                    <span>{tool.displayName}</span>
                  </Tooltip>
                </TableCell>
                <TableCell>
                  <Chip label={t(`tools.risk.${tool.effectiveRiskLevel}`)} color={RISK_COLOR[tool.effectiveRiskLevel]} size="small" />
                </TableCell>
                <TableCell>
                  <Stack direction="row" spacing={0.5} sx={{ flexWrap: 'wrap' }}>
                    {tool.requiredPermissions.map((permission) => (
                      <Chip key={permission} label={<bdi dir="ltr">{permission}</bdi>} size="small" variant="outlined" />
                    ))}
                  </Stack>
                </TableCell>
                <TableCell>
                  <Chip label={t(`tools.status.${tool.activationStatus}`)} color={STATUS_COLOR[tool.activationStatus]} size="small" />
                </TableCell>
                <TableCell sx={{ textAlign: 'end' }}>
                  {tool.activationStatus !== 'Active' ? (
                    <Tooltip title={t('tools.activate')}>
                      <IconButton
                        aria-label={t('tools.activateAria', { name: tool.displayName })}
                        color="success"
                        disabled={activateTool.isPending}
                        onClick={() =>
                          activateTool.mutate(
                            { serverId, toolId: tool.id, input: { effectiveRiskLevelOverride: null, requiredPermissionsJsonOverride: null } },
                            { onError: onMutationError(t('tools.errors.activate')) },
                          )
                        }
                      >
                        <CheckCircleIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  ) : (
                    <Tooltip title={t('tools.deactivate')}>
                      <IconButton
                        aria-label={t('tools.deactivateAria', { name: tool.displayName })}
                        color="error"
                        disabled={deactivateTool.isPending}
                        onClick={() =>
                          deactivateTool.mutate(
                            { serverId, toolId: tool.id },
                            { onError: onMutationError(t('tools.errors.deactivate')) },
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
