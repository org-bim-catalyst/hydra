import NotificationsIcon from '@mui/icons-material/Notifications'
import {
  Alert,
  Badge,
  Box,
  Button,
  IconButton,
  List,
  ListItem,
  ListItemText,
  Popover,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { useNotifications } from '../hooks/useDocuments'
import { useMarkNotificationRead } from '../hooks/useDocumentMutations'

function formatEventType(eventType: string): string {
  return eventType.replace(/([a-z])([A-Z])/g, '$1 $2')
}

/** FR-047, US6 — the notification inbox (fallback for anything missed while disconnected; `useNotificationHub` handles the live toast). */
export function NotificationInbox() {
  const [anchorEl, setAnchorEl] = useState<HTMLButtonElement | null>(null)
  const { data, isError } = useNotifications()
  const markRead = useMarkNotificationRead()

  const unreadCount = data?.items.filter((n) => !n.isRead).length ?? 0

  return (
    <>
      <IconButton aria-label="Notifications" onClick={(e) => setAnchorEl(e.currentTarget)}>
        <Badge badgeContent={unreadCount} color="error">
          <NotificationsIcon />
        </Badge>
      </IconButton>

      <Popover
        open={Boolean(anchorEl)}
        anchorEl={anchorEl}
        onClose={() => setAnchorEl(null)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
      >
        <Box sx={{ width: 360, maxHeight: 480, overflowY: 'auto', p: 1 }}>
          <Typography variant="subtitle2" sx={{ px: 1, py: 0.5 }}>
            Notifications
          </Typography>

          {isError && (
            <Alert severity="error" sx={{ m: 1 }}>
              Could not load notifications. Please try again.
            </Alert>
          )}

          {!isError && (data?.items.length ?? 0) === 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ px: 1, py: 1 }}>
              No notifications yet.
            </Typography>
          )}

          <List dense>
            {data?.items.map((notification) => (
              <ListItem
                key={notification.id}
                sx={{ bgcolor: notification.isRead ? 'transparent' : 'action.hover', borderRadius: 1 }}
                secondaryAction={
                  !notification.isRead && (
                    <Button size="small" onClick={() => markRead.mutate(notification.id)} disabled={markRead.isPending}>
                      Mark read
                    </Button>
                  )
                }
              >
                <ListItemText
                  primary={formatEventType(notification.eventType)}
                  secondary={`${notification.message} — ${new Date(notification.createdAtUtc).toLocaleString()}`}
                />
              </ListItem>
            ))}
          </List>
        </Box>
      </Popover>
    </>
  )
}
