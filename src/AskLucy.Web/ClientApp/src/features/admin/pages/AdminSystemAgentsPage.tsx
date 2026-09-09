import {
  Box,
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
  const { data: agents } = useQuery({
    queryKey: ADMIN_SYSTEM_AGENTS_QUERY_KEY,
    queryFn: adminSystemAgentsApi.getSystemAgents,
  })

  return (
    <AdminShell
      title="System agents"
      subtitle="The platform's own provisioned agents — read-only, never user-editable"
    >
      <Paper elevation={1}>
        <TableContainer>
          <Table>
            <TableHead>
              <TableRow>
                <TableCell>Name</TableCell>
                <TableCell>System key</TableCell>
                <TableCell>Status</TableCell>
                <TableCell>Version</TableCell>
                <TableCell>Last updated</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {agents?.length === 0 && (
                <TableRow>
                  <TableCell colSpan={5}>
                    <Box sx={{ py: 3, textAlign: 'center' }}>
                      <Typography variant="body2" color="text.secondary">
                        No system agents are currently provisioned.
                      </Typography>
                    </Box>
                  </TableCell>
                </TableRow>
              )}
              {agents?.map((agent) => (
                <TableRow key={agent.id} hover>
                  <TableCell>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                      <Typography variant="body2">{agent.name}</Typography>
                      <Chip label="Provisioned by Ask Lucy" size="small" color="primary" variant="outlined" />
                    </Box>
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
