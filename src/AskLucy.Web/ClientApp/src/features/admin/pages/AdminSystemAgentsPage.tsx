import {
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
import * as adminSystemAgentsApi from '../api/adminSystemAgentsApi'
import { AdminShell } from '../components/AdminShell'

const ADMIN_SYSTEM_AGENTS_QUERY_KEY = ['admin', 'system-agents']

/**
 * Admin-only, read-only visibility into the platform's system-provisioned agents
 * (specs/047-admin-system-agents), since the personal Agents page (`/agents`) is deliberately
 * scoped to the caller's own agents and can never show one of these (spec.md FR-048). Mirrors
 * AdminAiProvidersPage.tsx's table shape; no edit/delete/duplicate/publish affordance exists
 * here on purpose — this screen only observes what `SystemAgentProvisioner` already did.
 */
export function AdminSystemAgentsPage() {
  const { data: agents, isLoading } = useQuery({
    queryKey: ADMIN_SYSTEM_AGENTS_QUERY_KEY,
    queryFn: adminSystemAgentsApi.getSystemAgents,
  })

  // While the body holds only the empty-state row, stretch the table over the whole container so
  // that row centres in it instead of hugging the header. Not while loading: the skeleton rows
  // fill the body themselves, and stretching would smear six of them over the page.
  const showsStatusRow = !isLoading && (agents ?? []).length === 0

  // Keeps the container's bottom edge on a row boundary: no half-visible last row.
  const { ref: tableRef, maxHeight: tableMaxHeight } = useWholeRowScroll()

  return (
    <AdminShell
      title="System agents"
      subtitle="The platform's own provisioned agents — read-only, never user-editable"
    >
      <Paper elevation={1} sx={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <TableContainer ref={tableRef} sx={{ flex: 1, minHeight: 0, overflow: 'auto', maxHeight: tableMaxHeight }}>
          <Table sx={{ height: showsStatusRow ? '100%' : undefined }}>
            <TableHead>
              <TableRow>
                <TableCell>Name</TableCell>
                <TableCell>Origin</TableCell>
                <TableCell>System key</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Version</TableCell>
                <TableCell>Last updated</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {isLoading && <TableLoadingRow colSpan={6} />}
              {!isLoading && agents?.length === 0 && (
                <TableEmptyRow colSpan={6} message="No system agents are currently provisioned." />
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
                    <Chip label="Provisioned by Ask Lucy" size="small" color="primary" variant="outlined" />
                  </TableCell>
                  <TableCell>{agent.systemKey ?? '—'}</TableCell>
                  <TableCell>
                    <Chip size="small" label={agent.status} variant="outlined" />
                  </TableCell>
                  <TableCell>{agent.publishedVersionNumber ?? '—'}</TableCell>
                  <TableCell>{new Date(agent.lastUpdatedAtUtc).toLocaleString()}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      </Paper>
    </AdminShell>
  )
}
