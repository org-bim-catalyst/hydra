import {
  Alert,
  Box,
  Button,
  CircularProgress,
  Snackbar,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import { errorMessage } from '../../notifications/api/errorMessage'
import type { NotificationChannel } from '../../notifications/api/notificationPreferencesApi'
import type { NotificationCategory } from '../../notifications/api/notificationsApi'
import {
  useNotificationPreferences,
  useUpdateNotificationPreferences,
} from '../../notifications/hooks/useNotificationPreferences'

const CATEGORY_LABELS: Record<NotificationCategory, string> = {
  Security: 'Security',
  Account: 'Account',
  Agent: 'Agents',
  Workflow: 'Workflows',
  Document: 'Documents',
  KnowledgeBase: 'Knowledge bases',
  Memory: 'Memory',
  System: 'System announcements',
  Billing: 'Billing',
  Conversation: 'Conversations',
}

const CHANNELS: { channel: NotificationChannel; label: string }[] = [
  { channel: 'InApp', label: 'In-app' },
  { channel: 'Email', label: 'Email' },
]

const LOCKED_EXPLANATION = "Required: these notifications can't be turned off."

const visuallyHidden = {
  position: 'absolute',
  width: 1,
  height: 1,
  overflow: 'hidden',
  clip: 'rect(0 0 0 0)',
  whiteSpace: 'nowrap',
} as const

/**
 * specs/067 US4 (FR-031–FR-034) — which notifications the user gets, and where. Mandatory pairs are
 * shown locked; the server enforces that too, so a stale or forged request can't turn one off. Frequency
 * is "Immediate" only: digests exist in the model but aren't offered yet.
 */
export function NotificationPreferencesTab() {
  const { data, isPending, isError, error, refetch } = useNotificationPreferences()
  const update = useUpdateNotificationPreferences()

  if (isPending) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
        <CircularProgress aria-label="Loading notification preferences" />
      </Box>
    )
  }

  if (isError) {
    return (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={() => void refetch()}>
            Retry
          </Button>
        }
      >
        {errorMessage(error)}
      </Alert>
    )
  }

  return (
    <Stack spacing={2}>
      <Box>
        <Typography variant="h6">Notifications</Typography>
        <Typography variant="body2" color="text.secondary">
          Choose where you want to hear about each kind of event. Security and account notifications are
          required, so they stay on.
        </Typography>
      </Box>

      <TableContainer>
        <Table size="small" aria-label="Notification preferences">
          <TableHead>
            <TableRow>
              <TableCell>Category</TableCell>
              {CHANNELS.map(({ channel, label }) => (
                <TableCell key={channel} align="center">
                  {label}
                </TableCell>
              ))}
              <TableCell>Frequency</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data.categories.map((category) => {
              const categoryLabel = CATEGORY_LABELS[category.category]
              return (
                <TableRow key={category.category}>
                  <TableCell component="th" scope="row">
                    {categoryLabel}
                  </TableCell>
                  {CHANNELS.map(({ channel, label }) => {
                    const preference = category.channels.find((c) => c.channel === channel)
                    if (!preference) {
                      return (
                        <TableCell key={channel} align="center">
                          <span aria-hidden="true">—</span>
                          <Box component="span" sx={visuallyHidden}>
                            {`${categoryLabel} notifications are not sent by ${label}`}
                          </Box>
                        </TableCell>
                      )
                    }

                    const explanationId = `locked-${category.category}-${channel}`
                    return (
                      <TableCell key={channel} align="center">
                        <Tooltip title={preference.locked ? LOCKED_EXPLANATION : ''}>
                          {/* A disabled input doesn't take hover, so the tooltip sits on the wrapper. */}
                          <span>
                            <Switch
                              checked={preference.enabled}
                              disabled={preference.locked || update.isPending}
                              onChange={(event) =>
                                update.mutate([{ category: category.category, channel, enabled: event.target.checked }])
                              }
                              slotProps={{
                                input: {
                                  'aria-label': `${categoryLabel} notifications by ${label}`,
                                  ...(preference.locked ? { 'aria-describedby': explanationId } : {}),
                                },
                              }}
                            />
                            {preference.locked && (
                              <Box component="span" id={explanationId} sx={visuallyHidden}>
                                {LOCKED_EXPLANATION}
                              </Box>
                            )}
                          </span>
                        </Tooltip>
                      </TableCell>
                    )
                  })}
                  <TableCell>Immediate</TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </TableContainer>

      <Snackbar
        open={update.isError}
        autoHideDuration={8000}
        onClose={() => update.reset()}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity="error" variant="filled" onClose={() => update.reset()}>
          {`Your change wasn't saved. ${errorMessage(update.error)}`}
        </Alert>
      </Snackbar>
    </Stack>
  )
}
