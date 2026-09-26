import { Alert, Button, Link, Stack, Typography } from '@mui/material'
import { Link as RouterLink } from 'react-router'
import type { CorrectiveAction } from '../../api/adminOperationalFailuresApi'
import { useOpenHangfireDashboard } from '../../hooks/useOpenHangfireDashboard'

/**
 * The suggested fix (FR-015). A route renders as an in-app link; `OpenJobsDashboard` reuses the
 * nav's Jobs action, since the dashboard needs its session cookie minted before it opens.
 */
export function CorrectiveActionLink({ action }: { action: CorrectiveAction }) {
  const jobsDashboard = useOpenHangfireDashboard()

  if (action.adminRoute) {
    return (
      <Link component={RouterLink} to={action.adminRoute} variant="body2">
        {action.text}
      </Link>
    )
  }

  if (action.adminAction === 'OpenJobsDashboard') {
    return (
      <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
        <Button size="small" variant="outlined" disabled={jobsDashboard.isPending} onClick={() => void jobsDashboard.open()}>
          {action.text}
        </Button>
        {jobsDashboard.errorMessage && (
          <Alert severity="error" onClose={jobsDashboard.clearError}>
            {jobsDashboard.errorMessage}
          </Alert>
        )}
      </Stack>
    )
  }

  return <Typography variant="body2">{action.text}</Typography>
}
