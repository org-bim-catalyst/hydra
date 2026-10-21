import {
  Alert,
  Button,
  Chip,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useWholeRowScroll } from '../../../hooks/useWholeRowScroll'
import { TableEmptyRow } from '../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../components/TableLoadingRow'
import { ApiError } from '../../../api/httpClient'
import { useT } from '../../../i18n/useT'
import { useOuterT } from '../hooks/useOuterT'
import * as adminAiToolsApi from '../api/adminAiToolsApi'
import { AdminShell } from '../components/AdminShell'

const ADMIN_AI_TOOLS_QUERY_KEY = ['admin', 'ai-tools']

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
 */
export function AdminAiToolsPage() {
  const t = useT('admin.aiTools')
  const tc = useT('common')
  // The page renders outside the shell's language surface, so its title and subtitle use the
  // caller's effective language directly.
  const outerT = useOuterT('admin.aiTools')
  const { data: tools, error, isError, isLoading, refetch } = useQuery({
    queryKey: ADMIN_AI_TOOLS_QUERY_KEY,
    queryFn: adminAiToolsApi.getAiTools,
  })

  // The risk level is a server enum; an unknown future value is shown as it came rather than hidden.
  const riskLabel = (risk: string) =>
    RISK_LEVELS.includes(risk as RiskLevel) ? t(RISK_LABEL_KEYS[risk as RiskLevel]) : risk

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
              </TableRow>
            </TableHead>
            <TableBody>
              {isLoading && <TableLoadingRow colSpan={5} />}
              {!isLoading && tools?.length === 0 && (
                <TableEmptyRow colSpan={5} message={t('table.empty')} />
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
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>
    </AdminShell>
  )
}
