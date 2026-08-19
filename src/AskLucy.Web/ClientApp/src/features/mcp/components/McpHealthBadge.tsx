import { Chip, Tooltip } from '@mui/material'
import type { McpServerHealth, McpServerHealthStatus } from '../api/mcpServersApi'

const COLOR_BY_STATUS: Record<McpServerHealthStatus, 'success' | 'warning' | 'error' | 'default'> = {
  Healthy: 'success',
  Degraded: 'warning',
  Unavailable: 'error',
  AuthenticationFailed: 'error',
  ConfigurationError: 'error',
  Unknown: 'default',
}

const LABEL_BY_STATUS: Record<McpServerHealthStatus, string> = {
  Healthy: 'Healthy',
  Degraded: 'Degraded',
  Unavailable: 'Unavailable',
  AuthenticationFailed: 'Authentication failed',
  ConfigurationError: 'Configuration error',
  Unknown: 'Unknown',
}

/** spec.md FR-055/FR-056 — the six-state MCP server health status, color-coded (research.md Decision 13, polled not pushed). */
export function McpHealthBadge({ health }: { health: McpServerHealth | undefined }) {
  const status = health?.status ?? 'Unknown'
  const chip = <Chip label={LABEL_BY_STATUS[status]} color={COLOR_BY_STATUS[status]} size="small" />

  return health?.detail ? <Tooltip title={health.detail}>{chip}</Tooltip> : chip
}
