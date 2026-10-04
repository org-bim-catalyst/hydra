import { RiNotification3Line } from '@remixicon/react'
import { Badge, Fab } from '@mui/material'
import { NotificationBell } from '../../features/notifications/components/NotificationBell'
import { CIRCULAR_BUTTON_SX } from './circularActionChrome'

/**
 * The Studio workspace's notification bell. Only the trigger is local — the popover itself is
 * the one `NotificationBell` the rest of the app uses, mirroring `StudioAccountMenuButton`'s
 * same split, so the workspace keeps its circular chrome without owning a second unread-count
 * subscription or popover.
 */
export function StudioNotificationBellButton() {
  return (
    <NotificationBell
      renderTrigger={({ onClick, count }) => (
        <Fab
          size="small"
          aria-label={count > 0 ? `Notifications, ${count} unread` : 'Notifications'}
          onClick={onClick}
          sx={CIRCULAR_BUTTON_SX}
        >
          <Badge badgeContent={count} color="error" max={99}>
            <RiNotification3Line size={20} />
          </Badge>
        </Fab>
      )}
    />
  )
}
