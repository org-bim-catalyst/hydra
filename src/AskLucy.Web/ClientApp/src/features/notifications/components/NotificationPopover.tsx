import { Alert, Box, Button, CircularProgress, Divider, List, Popover, Snackbar, Stack, Typography } from '@mui/material'
import { Link as RouterLink, useLocation, useNavigate } from 'react-router'
import { EmptyState } from '../../../components/EmptyState'
import { fromPathState } from '../../../routes/viewLandingState'
import { errorMessage } from '../api/errorMessage'
import type { NotificationItem as NotificationItemDto } from '../api/notificationsApi'
import { useMarkAllNotificationsRead } from '../hooks/useNotificationMutations'
import { useNotifications } from '../hooks/useNotifications'
import { NotificationItem } from './NotificationItem'

export interface NotificationPopoverProps {
  anchorEl: HTMLElement | null
  onClose: () => void
}

/** T071 — the latest 10, a mark-all-read action, and a "View all" link to the full page. */
export function NotificationPopover({ anchorEl, onClose }: NotificationPopoverProps) {
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const open = anchorEl !== null
  const { data, isLoading, isError } = useNotifications({ limit: 10 })
  const markAllRead = useMarkAllNotificationsRead()

  const items = data?.pages[0]?.items ?? []

  const openItem = (item: NotificationItemDto) => {
    onClose()
    if (item.action && item.relatedItem?.available !== false) {
      navigate(item.action.route)
    }
  }

  return (
    <Popover
      open={open}
      anchorEl={anchorEl}
      onClose={onClose}
      anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
      transformOrigin={{ vertical: 'top', horizontal: 'right' }}
      slotProps={{ paper: { sx: { width: 380, maxHeight: 480 } } }}
    >
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', px: 2, py: 1 }}>
        <Typography variant="subtitle1">Notifications</Typography>
        <Button size="small" onClick={() => markAllRead.mutate(undefined)} disabled={markAllRead.isPending || items.length === 0}>
          Mark all read
        </Button>
      </Stack>
      <Divider />

      {isLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 3 }}>
          <CircularProgress size={20} />
        </Box>
      ) : isError ? (
        <Alert severity="error" sx={{ m: 2 }}>
          Couldn't load notifications.
        </Alert>
      ) : items.length === 0 ? (
        <EmptyState title="No notifications" description="You're all caught up." />
      ) : (
        <List disablePadding sx={{ overflowY: 'auto', maxHeight: 340 }}>
          {items.map((item) => (
            <NotificationItem key={item.id} item={item} onOpen={openItem} dense />
          ))}
        </List>
      )}

      <Divider />
      <Box sx={{ p: 1, textAlign: 'center' }}>
        <Button component={RouterLink} to="/notifications" state={fromPathState(pathname)} size="small" onClick={onClose}>
          View all
        </Button>
      </Box>

      <Snackbar open={markAllRead.isError} autoHideDuration={5000}>
        <Alert severity="error" variant="filled">
          {markAllRead.error ? errorMessage(markAllRead.error) : null}
        </Alert>
      </Snackbar>
    </Popover>
  )
}
