import { Box, ButtonBase, Card, CardActionArea, CardContent, Chip, Stack, Typography } from '@mui/material'
import { useNavigate } from 'react-router'
import type { NotificationItem as NotificationItemDto, NotificationPriority } from '../api/notificationsApi'
import { useFormat, useT } from '../../../i18n/useT'

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
 * payload must show up literally, not as bold). The title and message come from the server already in the
 * notification's own language (FR-044c); only the chrome around them is translated here.
 *
 * Not wrapped in its own `LocalizedSurface`: it is only ever rendered inside the popover or the page, which are.
 */
export function NotificationItem({ item, onOpen, dense = false }: NotificationItemProps) {
  const navigate = useNavigate()
  const t = useT('notifications')
  const tc = useT('common')
  const format = useFormat()
  const isUnread = item.readAtUtc === null

  const handleClick = () => {
    onOpen(item)
    if (item.action && item.relatedItem?.available !== false) {
      navigate(item.action.route)
    }
  }

  // The popover ("dense") rendering drops the page's card chrome (border, chip row) for a
  // compact, hover-highlighted row — the standard notification-dropdown pattern — since a
  // bordered card per row inside an already-boxed 380px popover reads as nested chrome.
  if (dense) {
    return (
      <ButtonBase
        data-testid="notification-card"
        data-unread={isUnread}
        onClick={handleClick}
        sx={{
          display: 'block',
          width: '100%',
          textAlign: 'start',
          px: 2,
          py: 1.25,
          borderRadius: 0,
          '&:hover': { bgcolor: 'action.hover' },
        }}
      >
        <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 1 }}>
          <Box sx={{ width: 8, flexShrink: 0, mt: 0.75 }}>
            {isUnread && (
              <Box aria-label={t('center.unread')} role="status" sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: 'primary.main' }} />
            )}
          </Box>
          <Box sx={{ flex: 1, minWidth: 0 }}>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <Typography variant="body2" noWrap sx={{ flex: 1, fontWeight: isUnread ? 700 : 500 }}>
                {item.title}
              </Typography>
              {(item.priority === 'High' || item.priority === 'Critical') && (
                <Box sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: `${PRIORITY_COLOR[item.priority]}.main`, flexShrink: 0 }} />
              )}
            </Stack>
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{ display: '-webkit-box', WebkitLineClamp: 2, WebkitBoxOrient: 'vertical', overflow: 'hidden', mt: 0.25 }}
            >
              {item.message}
            </Typography>
            <Typography variant="caption" color="text.disabled" sx={{ display: 'block', mt: 0.5 }}>
              {format.relative(item.createdAtUtc, { justNow: t('center.justNow') })}
              {item.relatedItem?.available === false && ` · ${tc('states.noLongerAvailable')}`}
            </Typography>
          </Box>
        </Stack>
      </ButtonBase>
    )
  }

  return (
    <Card data-testid="notification-card" data-unread={isUnread} variant="outlined" sx={{ mx: 2, mb: 1 }}>
      <CardActionArea onClick={handleClick}>
        <CardContent sx={{ py: 1.5 }}>
          <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 1 }}>
            {isUnread && (
              <Box
                aria-label={t('center.unread')}
                role="status"
                sx={{ width: 8, height: 8, borderRadius: '50%', bgcolor: 'primary.main', mt: 0.75, flexShrink: 0 }}
              />
            )}
            <Box sx={{ flex: 1, minWidth: 0 }}>
              <Typography variant="body1" sx={{ fontWeight: isUnread ? 700 : 400 }}>
                {item.title}
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25 }}>
                {item.message}
              </Typography>

              <Stack direction="row" spacing={1} sx={{ mt: 1.5, alignItems: 'center', flexWrap: 'wrap', gap: 1 }}>
                <Chip size="small" variant="outlined" label={t(`categories.${item.category}`)} />
                {(item.priority === 'High' || item.priority === 'Critical') && (
                  <Chip size="small" label={t(`priorities.${item.priority}`)} color={PRIORITY_COLOR[item.priority]} />
                )}
                {item.relatedItem?.available === false && (
                  <Chip size="small" variant="outlined" color="default" label={tc('states.noLongerAvailable')} />
                )}
                <Typography variant="caption" color="text.secondary">
                  {format.relative(item.createdAtUtc, { justNow: t('center.justNow') })}
                </Typography>
              </Stack>
            </Box>
          </Stack>
        </CardContent>
      </CardActionArea>
    </Card>
  )
}
