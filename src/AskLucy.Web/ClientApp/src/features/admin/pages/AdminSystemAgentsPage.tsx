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
import { useFormat, useT } from '../../../i18n/useT'
import * as adminSystemAgentsApi from '../api/adminSystemAgentsApi'
import { AdminShell } from '../components/AdminShell'

const ADMIN_SYSTEM_AGENTS_QUERY_KEY = ['admin', 'system-agents']

// The same numeric date and time `toLocaleString()` produced, so English reads as it always did.
const LAST_UPDATED_FORMAT: Intl.DateTimeFormatOptions = {
  year: 'numeric',
  month: 'numeric',
  day: 'numeric',
  hour: 'numeric',
  minute: 'numeric',
  second: 'numeric',
}

/**
 * Admin-only, read-only visibility into the platform's system-provisioned agents
 * (specs/047-admin-system-agents), since the personal Agents page (`/agents`) is deliberately
 * scoped to the caller's own agents and can never show one of these (spec.md FR-048). Mirrors
 * AdminAiProvidersPage.tsx's table shape; no edit/delete/duplicate/publish affordance exists
 * here on purpose — this screen only observes what `SystemAgentProvisioner` already did.
 */
export function AdminSystemAgentsPage() {
  const t = useT('admin.systemAgents')
  const tc = useT('common')
  const format = useFormat()
  const { data: agents, error, isError, isLoading, refetch } = useQuery({
    queryKey: ADMIN_SYSTEM_AGENTS_QUERY_KEY,
    queryFn: adminSystemAgentsApi.getSystemAgents,
  })

  // The status is a server enum; an unknown future value is shown as it came rather than hidden.
  const statusLabel = (status: string) =>
    status === 'Draft' || status === 'Published' || status === 'Archived'
      ? t(`status.${status}`)
      : status

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header. Not while loading: the skeleton rows
  // fill the body themselves, and stretching would smear six of them over the page.
  const showsStatusRow = !isLoading && (agents ?? []).length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll()

  return (
    <AdminShell
      title={t('title')}
      subtitle={t('subtitle')}
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
                <TableCell>{t('table.systemKey')}</TableCell>
                <TableCell>{t('table.status')}</TableCell>
                <TableCell>{t('table.version')}</TableCell>
                <TableCell>{t('table.lastUpdated')}</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {isLoading && <TableLoadingRow colSpan={6} />}
              {!isLoading && agents?.length === 0 && (
                <TableEmptyRow colSpan={6} message={t('table.empty')} />
              )}
              {agents?.map((agent) => (
                <TableRow key={agent.id} hover>
                  <TableCell>
                    <Typography variant="body2">{agent.name}</Typography>
                  </TableCell>
                  {/*
                    Its own column rather than a chip trailing the name: agent names vary wildly in
                    length here (one is a GUID suffix), so an inline badge landed at a different x
                    on every row and read as clutter instead of a column of like values.
                  */}
                  <TableCell>
                    <Chip label={t('table.provisioned')} size="small" color="primary" variant="outlined" />
                  </TableCell>
                  <TableCell>
                    <bdi dir="ltr">{agent.systemKey ?? '—'}</bdi>
                  </TableCell>
                  <TableCell>
                    <Chip size="small" label={statusLabel(agent.status)} variant="outlined" />
                  </TableCell>
                  <TableCell>
                    <bdi dir="ltr">
                      {agent.publishedVersionNumber === null
                        ? '—'
                        : format.number(agent.publishedVersionNumber)}
                    </bdi>
                  </TableCell>
                  <TableCell>
                    <bdi dir="ltr">{format.date(agent.lastUpdatedAtUtc, LAST_UPDATED_FORMAT)}</bdi>
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
