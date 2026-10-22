import { useState } from 'react'
import SettingsOutlinedIcon from '@mui/icons-material/SettingsOutlined'
import {
  Alert,
  Button,
  Chip,
  IconButton,
  Paper,
  Snackbar,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import { ApiError } from '../../../api/httpClient'
import { useT } from '../../../i18n/useT'
import { useOuterT } from '../hooks/useOuterT'
import * as adminAiToolsApi from '../api/adminAiToolsApi'
import type { ToolSettings } from '../api/adminAiToolsApi'
import { AdminShell } from '../components/AdminShell'
import { ToolSettingsDialog } from '../components/ToolSettingsDialog'

const ADMIN_AI_TOOLS_QUERY_KEY = ['admin', 'ai-tools']
const TOOL_SETTINGS_QUERY_KEY = ['admin', 'ai-tools', 'settings']

/** Pinned rather than auto-sized, so the column does not move as the rows change. */
const SETTINGS_COLUMN_WIDTH = 96
const COLUMN_COUNT = 6

const RISK_LEVELS = ['Low', 'Medium', 'High', 'Critical'] as const
type RiskLevel = (typeof RISK_LEVELS)[number]

const RISK_COLORS: Record<RiskLevel, 'default' | 'info' | 'warning' | 'error'> = {
  Low: 'default',
  Medium: 'info',
  High: 'warning',
  Critical: 'error',
}

const RISK_LABEL_KEYS: Record<RiskLevel, 'risk.Low' | 'risk.Medium' | 'risk.High' | 'risk.Critical'> = {
  Low: 'risk.Low',
  Medium: 'risk.Medium',
  High: 'risk.High',
  Critical: 'risk.Critical',
}

/**
 * Admin-only, read-only visibility into the tools the platform's agents can call (specs/083).
 *
 * What a deployment can call is a fact about the deployment, not a setting an administrator edits
 * here — so this screen only observes, exactly like System agents. The rows come from the same
 * merged catalogue the agent runtime dispatches on (native tools plus the currently-active MCP
 * tools), so it can never show a tool the runtime would refuse, or hide one it would use.
 *
 * The one thing that is a setting is the configuration a tool runs with by default — which
 * imagery provider a snapshot uses, how much it crops. Those live behind each tool's gear and
 * are changed with the manage permission; the catalogue itself stays read-only.
 */
export function AdminAiToolsPage() {
  const t = useT('admin.aiTools')
  const tc = useT('common')
  // The page renders outside the shell's language surface, so its title and subtitle use the
  // caller's effective language directly.
  const outerT = useOuterT('admin.aiTools')
  const queryClient = useQueryClient()
  const { data: tools, error, isError, isLoading, refetch } = useQuery({
    queryKey: ADMIN_AI_TOOLS_QUERY_KEY,
    queryFn: adminAiToolsApi.getAiTools,
  })

  // specs/083 — which tools declare settings behind their gear. Fetched once for the whole
  // table; until it arrives, or if it fails, every gear stays disabled rather than guessing.
  const {
    data: toolSettings,
    isLoading: settingsLoading,
    isError: settingsFailed,
    refetch: refetchSettings,
  } = useQuery({
    queryKey: TOOL_SETTINGS_QUERY_KEY,
    queryFn: adminAiToolsApi.getToolSettings,
  })

  const [feedback, setFeedback] = useState<{
    severity: 'success' | 'error'
    message: string
  } | null>(null)
  const [configuring, setConfiguring] = useState<ToolSettings | null>(null)

  // The risk level is a server enum; an unknown future value is shown as it came rather than hidden.
  const riskLabel = (risk: string) =>
    RISK_LEVELS.includes(risk as RiskLevel) ? t(RISK_LABEL_KEYS[risk as RiskLevel]) : risk

  /**
   * The settings a tool declares, or null when it declares none. A failed or unfinished fetch
   * leaves every gear disabled rather than pretending a tool has nothing to configure.
   */
  const settingsFor = (toolName: string): ToolSettings | null =>
    settingsLoading || settingsFailed
      ? null
      : (toolSettings ?? []).find((settings) => settings.tool === toolName) ?? null

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header.
  const showsStatusRow = !isLoading && (tools ?? []).length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll()

  return (
    <AdminShell
      title={outerT('title')}
      subtitle={outerT('subtitle')}
    >
      {isError && (
        <Alert
          severity="error"
          sx={{ mb: 2 }}
          action={<Button onClick={() => refetch()}>{tc('actions.retry')}</Button>}
        >
          {error instanceof ApiError ? (error.detail ?? error.message) : t('errors.load')}
        </Alert>
      )}
      {settingsFailed && (
        <Alert
          severity="error"
          sx={{ mb: 2 }}
          action={
            <Button color="inherit" size="small" onClick={() => void refetchSettings()}>
              {t('retry')}
            </Button>
          }
        >
          {t('settingsLoadFailed')}
        </Alert>
      )}
      <Paper elevation={1} sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <TableContainer ref={tableRef} sx={{ flex: 1, minHeight: 0, overflow: 'auto', maxHeight: tableMaxHeight }}>
          <Table sx={{ height: showsStatusRow ? '100%' : undefined }}>
            <TableHead>
              <TableRow>
                <TableCell>{t('table.name')}</TableCell>
                <TableCell>{t('table.origin')}</TableCell>
                <TableCell>{t('table.description')}</TableCell>
                <TableCell>{t('table.risk')}</TableCell>
                <TableCell>{t('table.permissions')}</TableCell>
                <TableCell align="center" sx={{ width: SETTINGS_COLUMN_WIDTH }}>
                  {t('columns.settings')}
                </TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {isLoading && <TableLoadingRow colSpan={COLUMN_COUNT} />}
              {!isLoading && tools?.length === 0 && (
                <TableEmptyRow colSpan={COLUMN_COUNT} message={t('table.empty')} />
              )}
              {tools?.map((tool) => (
                <TableRow key={tool.name} hover>
                  <TableCell>
                    <Typography variant="body2">
                      <bdi dir="ltr">{tool.name}</bdi>
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Chip
                      label={adminAiToolsApi.isMcpTool(tool.name) ? t('origin.mcp') : t('origin.native')}
                      size="small"
                      variant="outlined"
                    />
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2" color="text.secondary">
                      {tool.description}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Chip
                      size="small"
                      label={riskLabel(tool.riskLevel)}
                      color={RISK_COLORS[tool.riskLevel as RiskLevel] ?? 'default'}
                      variant="outlined"
                    />
                  </TableCell>
                  <TableCell>
                    {tool.requiredPermissions.length === 0 ? (
                      '—'
                    ) : (
                      <Typography variant="body2" color="text.secondary">
                        {tool.requiredPermissions.join(', ')}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell align="center" sx={{ width: SETTINGS_COLUMN_WIDTH }}>
                    <ToolSettingsButton
                      toolName={tool.name}
                      settings={settingsFor(tool.name)}
                      onConfigure={setConfiguring}
                    />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert
          severity={feedback?.severity ?? 'info'}
          variant="filled"
          onClose={() => setFeedback(null)}
        >
          {feedback?.message}
        </Alert>
      </Snackbar>

      <ToolSettingsDialog
        toolSettings={configuring}
        onClose={() => setConfiguring(null)}
        onSaved={() => {
          setConfiguring(null)
          void queryClient.invalidateQueries({ queryKey: TOOL_SETTINGS_QUERY_KEY })
          setFeedback({ severity: 'success', message: t('feedback.settingsSaved') })
        }}
        onFailed={(message) => setFeedback({ severity: 'error', message })}
      />
    </AdminShell>
  )
}

/**
 * specs/083 — every row has a gear so the column reads the same all the way down; it is dimmed
 * and disabled where the tool has nothing to configure. The tooltip sits on a wrapper span
 * because a disabled button fires no pointer events of its own.
 */
function ToolSettingsButton({
  toolName,
  settings,
  onConfigure,
}: {
  toolName: string
  settings: ToolSettings | null
  onConfigure: (settings: ToolSettings) => void
}) {
  const t = useT('admin.aiTools')
  const configurable = settings !== null && settings.settings.length > 0
  return (
    <Tooltip
      title={configurable ? t('row.configure', { tool: toolName }) : t('row.nothingToConfigure')}
    >
      <span>
        <IconButton
          size="small"
          aria-label={t('row.settingsFor', { tool: toolName })}
          disabled={!configurable}
          onClick={() => configurable && settings && onConfigure(settings)}
        >
          <SettingsOutlinedIcon fontSize="small" />
        </IconButton>
      </span>
    </Tooltip>
  )
}
