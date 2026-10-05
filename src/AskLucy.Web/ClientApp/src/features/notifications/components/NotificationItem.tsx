import { Box, Card, CardActionArea, CardContent, Chip, Stack, Typography } from '@mui/material'
import { useNavigate } from 'react-router'
import type { NotificationCategory, NotificationItem as NotificationItemDto, NotificationPriority } from '../api/notificationsApi'
import { formatRelativeTime } from '../utils/relativeTime'

const PRIORITY_COLOR: Record<NotificationPriority, 'default' | 'info' | 'warning' | 'error'> = {
  Low: 'default',
  Normal: 'info',
  High: 'warning',
  Critical: 'error',
}

const CATEGORY_LABEL: Record<NotificationCategory, string> = {
  Security: 'Security',
  Account: 'Account',
  Agent: 'Agent',
  Workflow: 'Workflow',
  Document: 'Document',
  KnowledgeBase: 'Knowledge base',
  Memory: 'Memory',
  System: 'System',
  Billing: 'Billing',
  Conversation: 'Conversation',
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
    <Card data-testid="notification-card" data-unread={isUnread} variant="outlined" sx={{ mx: dense ? 0 : 2, mb: dense ? 0 : 1 }}>
      <CardActionArea onClick={handleClick}>
        <CardContent sx={{ py: dense ? 1 : 1.5 }}>
          <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 1 }}>
            {isUnread && (
              <Box
                aria-label="Unread"
                role="status"
                sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: 'primary.main', mt: 0.75, flexShrink: 0 }}
              />
            )}
            <Box sx={{ flex: 1, minWidth: 0 }}>
              <Typography variant={dense ? 'body2' : 'body1'} sx={{ fontWeight: isUnread ? 700 : 400 }}>
                {item.title}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25 }}>
                {item.message}
              </Typography>

              <Stack direction="row" spacing={1} sx={{ mt: 1.5, alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
                <Chip size="small" variant="outlined" label={CATEGORY_LABEL[item.category]} />
                {(item.priority === 'High' || item.priority === 'Critical') && (
                  <Chip size="small" label={item.priority} color={PRIORITY_COLOR[item.priority]} />
                )}
                {item.relatedItem?.available === false && (
                  <Chip size="small" variant="outlined" color="default" label="No longer available" />
                )}
                <Typography variant="caption" color="text.secondary">
                  {formatRelativeTime(item.createdAtUtc)}
                </Typography>
              </Stack>
            </Box>
          </Stack>
        </CardContent>
      </CardActionArea>
    </Card>
  )
}
