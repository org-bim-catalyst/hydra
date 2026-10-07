import { Alert, Button, Grid, Paper, Skeleton, Typography } from '@mui/material'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import { useFormat, useT } from '../../../i18n/useT'
import { AdminShell } from '../components/AdminShell'
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
  // The page body reads the language, so it must sit inside the surface; AdminShell's own surface wraps only its children.
  return (
    <LocalizedSurface scope="subtree">
      <AdminDashboardPageContent />
    </LocalizedSurface>
  )
}

function AdminDashboardPageContent() {
  const t = useT('admin.dashboard')
  const format = useFormat()
  const { data: summary, isLoading, isError, refetch } = useAdminDashboard()

  return (
    <AdminShell title={t('title')} subtitle={t('subtitle')}>
      {isError && !summary ? (
        <Alert
          severity="error"
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                void refetch()
              }}
            >
              {t('retry')}
            </Button>
          }
        >
          {t('loadFailed')}
        </Alert>
      ) : isLoading || !summary ? (
        <Skeleton variant="rounded" height={400} />
      ) : (
        <Grid container spacing={2}>
          <Grid size={{ xs: 6, sm: 3 }}>
            <StatTile label={t('stats.totalUsers')} value={format.number(summary.totalUsers)} />
          </Grid>
          <Grid size={{ xs: 6, sm: 3 }}>
            <StatTile
              label={t('stats.twoFactorAdoption')}
              value={
                summary.totalUsers === 0
                  ? '—'
                  : `${Math.round((summary.twoFactorEnabledUsers / summary.totalUsers) * 100)}%`
              }
            />
          </Grid>
          <Grid size={{ xs: 6, sm: 3 }}>
            <StatTile label={t('stats.activeUsers')} value={format.number(summary.activeUsers)} />
          </Grid>
          <Grid size={{ xs: 6, sm: 3 }}>
            <StatTile label={t('stats.lockedOut')} value={format.number(summary.lockedOutUsers)} />
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
                title={t('split.activeVsLocked.title')}
                primaryLabel={t('split.activeVsLocked.primary')}
                primaryCount={summary.activeUsers}
                secondaryLabel={t('split.activeVsLocked.secondary')}
                secondaryCount={summary.lockedOutUsers}
              />
            </Paper>
          </Grid>
          <Grid size={{ xs: 12, md: 4 }}>
            <Paper elevation={1} sx={{ p: 2, height: '100%' }}>
              <StatusSplitChart
                title={t('split.confirmedVsPending.title')}
                primaryLabel={t('split.confirmedVsPending.primary')}
                primaryCount={summary.emailConfirmedUsers}
                secondaryLabel={t('split.confirmedVsPending.secondary')}
                secondaryCount={summary.emailPendingUsers}
              />
            </Paper>
          </Grid>
        </Grid>
      )}
    </AdminShell>
  )
}
