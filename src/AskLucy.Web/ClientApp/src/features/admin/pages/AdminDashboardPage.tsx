import { Button, Grid, Paper, Skeleton, Stack, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { useAdminDashboard } from '../hooks/useAdminDashboard'
import { NewUsersTrendChart } from '../charts/NewUsersTrendChart'
import { RoleDistributionChart } from '../charts/RoleDistributionChart'
import { StatusSplitChart } from '../charts/StatusSplitChart'

function StatTile({ label, value }: { label: string; value: string }) {
  return (
    <Paper elevation={1} sx={{ p: 2 }}>
      <Typography variant="body2" color="text.secondary">
        {label}
      </Typography>
      {/* Styled like a heading but not one semantically — the page's only true heading
          hierarchy is the h5 page title followed by each chart's h6 subtitle (axe
          heading-order); a stat number is not a document-outline heading. */}
      <Typography variant="h4" component="p">
        {value}
      </Typography>
    </Paper>
  )
}

/**
 * Admin Dashboard home (specs/001-admin-dashboard FR-001 through FR-007) — restores the
 * legacy Control Panel's landing page, modernized with live d3.js charts instead of a
 * static card-tile grid.
 */
export function AdminDashboardPage() {
  const { data: summary, isLoading } = useAdminDashboard()

  return (
    <AppShell
      title="Admin Dashboard"
      subtitle="Platform health and usage at a glance"
      actions={
        <Stack direction="row" spacing={1}>
          <Button component={RouterLink} to="/admin/users" variant="outlined" size="small">
            Manage users
          </Button>
          <Button component={RouterLink} to="/admin/ai-providers" variant="outlined" size="small">
            Manage AI providers
          </Button>
          <Button component={RouterLink} to="/admin/ai-capabilities" variant="outlined" size="small">
            Manage AI capabilities
          </Button>
          <Button component={RouterLink} to="/admin/agent-policies" variant="outlined" size="small">
            Manage agent policies
          </Button>
          <Button component={RouterLink} to="/admin/workflow-policies" variant="outlined" size="small">
            Manage workflow policies
          </Button>
          <Button component={RouterLink} to="/admin/mcp-servers" variant="outlined" size="small">
            Manage MCP servers
          </Button>
        </Stack>
      }
    >
      {isLoading || !summary ? (
        <Skeleton variant="rounded" height={400} />
      ) : (
        <Grid container spacing={2}>
          <Grid size={{ xs: 6, sm: 3 }}>
            <StatTile label="Total users" value={summary.totalUsers.toString()} />
          </Grid>
          <Grid size={{ xs: 6, sm: 3 }}>
            <StatTile
              label="2FA adoption"
              value={
                summary.totalUsers === 0
                  ? '—'
                  : `${Math.round((summary.twoFactorEnabledUsers / summary.totalUsers) * 100)}%`
              }
            />
          </Grid>
          <Grid size={{ xs: 6, sm: 3 }}>
            <StatTile label="Active users" value={summary.activeUsers.toString()} />
          </Grid>
          <Grid size={{ xs: 6, sm: 3 }}>
            <StatTile label="Locked out" value={summary.lockedOutUsers.toString()} />
          </Grid>

          <Grid size={12}>
            <Paper elevation={1} sx={{ p: 2 }}>
              <NewUsersTrendChart data={summary.newUsersLast30Days} />
            </Paper>
          </Grid>

          <Grid size={{ xs: 12, md: 4 }}>
            <Paper elevation={1} sx={{ p: 2, height: '100%' }}>
              <RoleDistributionChart data={summary.roleDistribution} />
            </Paper>
          </Grid>
          <Grid size={{ xs: 12, md: 4 }}>
            <Paper elevation={1} sx={{ p: 2, height: '100%' }}>
              <StatusSplitChart
                title="Active vs. locked out"
                primaryLabel="Active"
                primaryCount={summary.activeUsers}
                secondaryLabel="Locked out"
                secondaryCount={summary.lockedOutUsers}
              />
            </Paper>
          </Grid>
          <Grid size={{ xs: 12, md: 4 }}>
            <Paper elevation={1} sx={{ p: 2, height: '100%' }}>
              <StatusSplitChart
                title="Email confirmed vs. pending"
                primaryLabel="Confirmed"
                primaryCount={summary.emailConfirmedUsers}
                secondaryLabel="Pending"
                secondaryCount={summary.emailPendingUsers}
              />
            </Paper>
          </Grid>
        </Grid>
      )}
    </AppShell>
  )
}
