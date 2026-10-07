import NotificationsIcon from '@mui/icons-material/Notifications'
import { Badge, IconButton } from '@mui/material'
import { useState } from 'react'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import { useT } from '../../../i18n/useT'
import { useUnreadCount } from '../hooks/useNotifications'
import { NotificationPopover } from './NotificationPopover'

interface NotificationBellProps {
  /**
   * An alternative trigger, given an onClick, the unread count and a ready-made badge. The
   * Studio workspace passes its own circular Fab so the floating cluster keeps its own visual
   * language, mirroring `UserMenu`'s `renderTrigger` — everywhere else the default icon button
   * is used.
   */
  renderTrigger?: (props: { onClick: (event: React.MouseEvent<HTMLElement>) => void; count: number }) => React.ReactElement
}

/** T071 — the aria-label includes the count so a screen reader announces it without opening the popover. */
export function NotificationBell(props: NotificationBellProps = {}) {
  return (
    <LocalizedSurface scope="subtree">
      <NotificationBellContent {...props} />
    </LocalizedSurface>
  )
}

function NotificationBellContent({ renderTrigger }: NotificationBellProps) {
  const t = useT('notifications')
  const { data } = useUnreadCount()
  const count = data?.count ?? 0
  const [anchorEl, setAnchorEl] = useState<HTMLElement | null>(null)

  return (
    <>
      {renderTrigger ? (
        renderTrigger({ onClick: (e) => setAnchorEl(e.currentTarget), count })
      ) : (
        <IconButton
          color="inherit"
          aria-label={count > 0 ? t('bell.labelUnread', { count }) : t('bell.label')}
          onClick={(e) => setAnchorEl(e.currentTarget)}
        >
          <Badge badgeContent={count} color="error" max={99}>
            <NotificationsIcon />
          </Badge>
        </IconButton>
      )}
      <NotificationPopover anchorEl={anchorEl} onClose={() => setAnchorEl(null)} />
    </>
  )
}
