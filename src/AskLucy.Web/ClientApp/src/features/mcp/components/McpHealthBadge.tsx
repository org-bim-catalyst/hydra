import { Chip, Tooltip } from '@mui/material'
import { useT } from '../../../i18n/useT'
import type { McpServerHealth, McpServerHealthStatus } from '../api/mcpServersApi'

const COLOR_BY_STATUS: Record<McpServerHealthStatus, 'success' | 'warning' | 'error' | 'default'> = {
  Healthy: 'success',
  Degraded: 'warning',
  Unavailable: 'error',
  AuthenticationFailed: 'error',
  ConfigurationError: 'error',
  Unknown: 'default',
}

/** spec.md FR-055/FR-056 — the six-state MCP server health status, color-coded (research.md Decision 13, polled not pushed). */
export function McpHealthBadge({ health }: { health: McpServerHealth | undefined }) {
  const t = useT('admin.mcpServers')
  const status = health?.status ?? 'Unknown'
  const chip = <Chip label={t(`health.${status}`)} color={COLOR_BY_STATUS[status]} size="small" />

  return health?.detail ? <Tooltip title={health.detail}>{chip}</Tooltip> : chip
}
