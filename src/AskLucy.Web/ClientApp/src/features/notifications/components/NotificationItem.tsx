import { Box, Chip, ListItemButton, ListItemText, Stack, Typography } from '@mui/material'
import { useNavigate } from 'react-router'
import type { NotificationItem as NotificationItemDto, NotificationPriority } from '../api/notificationsApi'
import { formatRelativeTime } from '../utils/relativeTime'

const PRIORITY_COLOR: Record<NotificationPriority, 'default' | 'info' | 'warning' | 'error'> = {
  Low: 'default',
  Normal: 'info',
  High: 'warning',
  Critical: 'error',
}

export interface NotificationItemProps {
  item: NotificationItemDto
  onOpen: (item: NotificationItemDto) => void
  dense?: boolean
}

/**
 * T069 — title and message are rendered through JSX text nodes only, never
 * `dangerouslySetInnerHTML` (contracts/notifications-api.md: they are plain text, and a `<b>`
 * payload must show up literally, not as bold).
 */
export function NotificationItem({ item, onOpen, dense = false }: NotificationItemProps) {
  const navigate = useNavigate()
  const isUnread = item.readAtUtc === null

  const handleClick = () => {
    onOpen(item)
    if (item.action && item.relatedItem?.available !== false) {
      navigate(item.action.route)
    }
  }

  return (
    <ListItemButton onClick={handleClick} alignItems="flex-start" data-unread={isUnread} sx={{ gap: 1 }}>
      {isUnread && (
        <Box
          aria-label="Unread"
          role="status"
          sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: 'primary.main', mt: 1, flexShrink: 0 }}
        />
      )}
      <ListItemText
        primary={
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <Typography variant={dense ? 'body2' : 'subtitle2'} sx={{ fontWeight: isUnread ? 700 : 400 }}>
              {item.title}
            </Typography>
            {(item.priority === 'High' || item.priority === 'Critical') && (
              <Chip label={item.priority} size="small" color={PRIORITY_COLOR[item.priority]} />
            )}
          </Stack>
        }
        secondary={
          <>
            <Typography variant="body2" color="text.secondary" component="span" sx={{ display: 'block' }}>
              {item.message}
            </Typography>
            <Typography variant="caption" color="text.disabled" component="span">
              {formatRelativeTime(item.createdAtUtc)}
            </Typography>
            {item.relatedItem?.available === false && (
              <Typography variant="caption" color="text.disabled" component="span" sx={{ display: 'block' }}>
                No longer available
              </Typography>
            )}
          </>
        }
      />
    </ListItemButton>
  )
}
