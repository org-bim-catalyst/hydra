import { Alert, Box, Button, Chip, CircularProgress, Snackbar, Stack, Typography } from '@mui/material'
import { useNavigate } from 'react-router'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import { useFormat, useT } from '../../../i18n/useT'
import { errorMessage } from '../api/errorMessage'
import type { NotificationDetail, NotificationPriority } from '../api/notificationsApi'
import { useDeleteNotification, useMarkNotificationRead } from '../hooks/useNotificationMutations'

const PRIORITY_COLOR: Record<NotificationPriority, 'default' | 'info' | 'warning' | 'error'> = {
  Low: 'default',
  Normal: 'info',
  High: 'warning',
  Critical: 'error',
}

export interface NotificationDetailsProps {
  notification: NotificationDetail | undefined
  isLoading: boolean
  isError: boolean
  onDeleted?: () => void
}

/**
 * T069 — same plain-text rule as `NotificationItem`: title/message/metadata values render as text
 * nodes only. The action button navigates when the related item is still available, or shows "no
 * longer available" instead (contracts/notifications-api.md).
 */
export function NotificationDetails(props: NotificationDetailsProps) {
  return (
    <LocalizedSurface scope="subtree">
      <NotificationDetailsContent {...props} />
    </LocalizedSurface>
  )
}

function NotificationDetailsContent({ notification, isLoading, isError, onDeleted }: NotificationDetailsProps) {
  const navigate = useNavigate()
  const t = useT('notifications')
  const tc = useT('common')
  const format = useFormat()
  const markRead = useMarkNotificationRead()
  const deleteNotification = useDeleteNotification()

  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
        <CircularProgress size={24} />
      </Box>
    )
  }

  if (isError || !notification) {
    return (
      <Alert severity="error" sx={{ m: 2 }}>
        {t('details.loadFailed')}
      </Alert>
    )
  }

  const isUnavailable = notification.relatedItem?.available === false
  const mutationError = markRead.error ?? deleteNotification.error

  return (
    <Stack spacing={2} sx={{ p: 2 }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        <Typography variant="h6">{notification.title}</Typography>
        <Chip label={t(`priorities.${notification.priority}`)} size="small" color={PRIORITY_COLOR[notification.priority]} />
      </Stack>
      <Typography variant="body1">{notification.message}</Typography>
      <Typography variant="caption" color="text.secondary">
        {format.relative(notification.createdAtUtc, { justNow: t('center.justNow') })}
      </Typography>

      {Object.keys(notification.metadata).length > 0 && (
        <Stack spacing={0.5}>
          {Object.entries(notification.metadata).map(([key, value]) => (
            <Typography key={key} variant="body2" color="text.secondary">
              {key}: {value}
            </Typography>
          ))}
        </Stack>
      )}

      <Stack direction="row" spacing={1}>
        {notification.readAtUtc === null && (
          <Button size="small" onClick={() => markRead.mutate(notification.id)} disabled={markRead.isPending}>
            {t('details.markRead')}
          </Button>
        )}
        {notification.action &&
          (isUnavailable ? (
            <Typography variant="body2" color="text.disabled">
              {tc('states.noLongerAvailable')}
            </Typography>
          ) : (
            <Button size="small" variant="contained" onClick={() => navigate(notification.action!.route)}>
              {notification.action.label}
            </Button>
          ))}
        <Button
          size="small"
          color="error"
          disabled={deleteNotification.isPending}
          onClick={() => deleteNotification.mutate(notification.id, { onSuccess: onDeleted })}
        >
          {tc('actions.delete')}
        </Button>
      </Stack>

      <Snackbar open={mutationError !== null} autoHideDuration={5000}>
        <Alert severity="error" variant="filled">
          {mutationError ? errorMessage(mutationError, tc('errors.generic')) : null}
        </Alert>
      </Snackbar>
    </Stack>
  )
}
