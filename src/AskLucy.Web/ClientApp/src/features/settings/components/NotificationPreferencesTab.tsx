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
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import { useT } from '../../../i18n/useT'
import { errorMessage } from '../../notifications/api/errorMessage'
import type { NotificationChannel } from '../../notifications/api/notificationPreferencesApi'
import {
  useNotificationPreferences,
  useUpdateNotificationPreferences,
} from '../../notifications/hooks/useNotificationPreferences'

const CHANNELS: NotificationChannel[] = ['InApp', 'Email']

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
  return (
    <LocalizedSurface scope="subtree">
      <NotificationPreferencesContent />
    </LocalizedSurface>
  )
}

function NotificationPreferencesContent() {
  const t = useT('notifications')
  const tc = useT('common')
  const { data, isPending, isError, error, refetch } = useNotificationPreferences()
  const update = useUpdateNotificationPreferences()

  if (isPending) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
        <CircularProgress aria-label={t('preferences.loadingLabel')} />
      </Box>
    )
  }

  if (isError) {
    return (
      <Alert
        severity="error"
        action={
          <Button color="inherit" size="small" onClick={() => void refetch()}>
            {tc('actions.retry')}
          </Button>
        }
      >
        {errorMessage(error, tc('errors.generic'))}
      </Alert>
    )
  }

  return (
    <Stack spacing={2}>
      <Box>
        <Typography variant="h6">{t('preferences.title')}</Typography>
        <Typography variant="body2" color="text.secondary">
          {t('preferences.description')}
        </Typography>
      </Box>

      <TableContainer>
        <Table size="small" aria-label={t('preferences.tableLabel')}>
          <TableHead>
            <TableRow>
              <TableCell>{t('preferences.columns.category')}</TableCell>
              {CHANNELS.map((channel) => (
                <TableCell key={channel} align="center">
                  {t(`preferences.channels.${channel}`)}
                </TableCell>
              ))}
              <TableCell>{t('preferences.columns.frequency')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {data.categories.map((category) => {
              const categoryLabel = t(`preferences.categories.${category.category}`)
              return (
                <TableRow key={category.category}>
                  <TableCell component="th" scope="row">
                    {categoryLabel}
                  </TableCell>
                  {CHANNELS.map((channel) => {
                    const label = t(`preferences.channels.${channel}`)
                    const preference = category.channels.find((c) => c.channel === channel)
                    if (!preference) {
                      return (
                        <TableCell key={channel} align="center">
                          <span aria-hidden="true">—</span>
                          <Box component="span" sx={visuallyHidden}>
                            {t('preferences.notSentBy', { category: categoryLabel, channel: label })}
                          </Box>
                        </TableCell>
                      )
                    }

                    const explanationId = `locked-${category.category}-${channel}`
                    return (
                      <TableCell key={channel} align="center">
                        <Tooltip title={preference.locked ? t('preferences.locked') : ''}>
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
                                  'aria-label': t('preferences.switchLabel', { category: categoryLabel, channel: label }),
                                  ...(preference.locked ? { 'aria-describedby': explanationId } : {}),
                                },
                              }}
                            />
                            {preference.locked && (
                              <Box component="span" id={explanationId} sx={visuallyHidden}>
                                {t('preferences.locked')}
                              </Box>
                            )}
                          </span>
                        </Tooltip>
                      </TableCell>
                    )
                  })}
                  <TableCell>{t('preferences.frequencyImmediate')}</TableCell>
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
          {t('preferences.saveFailed', { detail: errorMessage(update.error, tc('errors.generic')) })}
        </Alert>
      </Snackbar>
    </Stack>
  )
}
