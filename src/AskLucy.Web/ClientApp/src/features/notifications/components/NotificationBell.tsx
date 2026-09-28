import NotificationsIcon from '@mui/icons-material/Notifications'
import { Badge, IconButton } from '@mui/material'
import { useState } from 'react'
import { useUnreadCount } from '../hooks/useNotifications'
import { NotificationPopover } from './NotificationPopover'

/** T071 — the aria-label includes the count so a screen reader announces it without opening the popover. */
export function NotificationBell() {
  const { data } = useUnreadCount()
  const count = data?.count ?? 0
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)

  return (
    <>
      <IconButton
        color="inherit"
        aria-label={count > 0 ? `Notifications, ${count} unread` : 'Notifications'}
        onClick={(e) => setAnchorEl(e.currentTarget)}
      >
        <Badge badgeContent={count} color="error" max={99}>
          <NotificationsIcon />
        </Badge>
      </IconButton>
      <NotificationPopover anchorEl={anchorEl} onClose={() => setAnchorEl(null)} />
    </>
  )
}
