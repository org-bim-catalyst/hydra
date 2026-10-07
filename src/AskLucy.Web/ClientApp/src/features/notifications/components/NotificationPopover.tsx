import { Alert, Box, Button, CircularProgress, Divider, List, Popover, Snackbar, Stack, Typography, useTheme } from '@mui/material'
import { Link as RouterLink, useLocation, useNavigate } from 'react-router'
import { EmptyState } from '../../../components/EmptyState'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import { useT } from '../../../i18n/useT'
import { fromPathState } from '../../../routes/viewLandingState'
import { overlaySurface } from '../../../theme/tokens/overlaySurface'
import { zIndex } from '../../../theme/tokens/zIndex'
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
export function NotificationPopover(props: NotificationPopoverProps) {
  return (
    <LocalizedSurface scope="subtree">
      <NotificationPopoverContent {...props} />
    </LocalizedSurface>
  )
}

function NotificationPopoverContent({ anchorEl, onClose }: NotificationPopoverProps) {
  const t = useT('notifications')
  const tc = useT('common')
  // The popover hangs from the bell at the header's end edge: the right in LTR, the left in RTL.
  const edge = useTheme().direction === 'rtl' ? 'left' : 'right'
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
      anchorOrigin={{ vertical: 'bottom', horizontal: edge }}
      transformOrigin={{ vertical: 'top', horizontal: edge }}
      // Matches UserMenu's own overlaySurface treatment exactly (offset, radius, border,
      // shadow) so the two menus anchored to the same Studio cluster — and the same AppShell
      // header — read as one family rather than two differently-chromed popups.
      sx={{ zIndex: zIndex.dropdown }}
      transitionDuration={overlaySurface.enterDurationMs}
      disableScrollLock
      slotProps={{
        paper: {
          elevation: 0,
          sx: {
            mt: `${overlaySurface.menuOffset}px`,
            width: 400,
            maxHeight: 480,
            borderRadius: `${overlaySurface.panelRadius}px`,
            overflow: 'hidden',
            bgcolor: 'background.paper',
            backgroundImage: 'none',
            boxShadow: overlaySurface.menuShadow,
            transformOrigin: `top ${edge}`,
          },
        },
      }}
    >
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', px: 2, py: 1.5 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>
          {t('center.title')}
        </Typography>
        <Button size="small" onClick={() => markAllRead.mutate(undefined)} disabled={markAllRead.isPending || items.length === 0}>
          {t('center.markAllRead')}
        </Button>
      </Stack>
      <Divider />

      {isLoading ? (
        <Box sx={{ display: 'flex', justifyContent: 'center', p: 3 }}>
          <CircularProgress size={20} />
        </Box>
      ) : isError ? (
        <Alert severity="error" sx={{ m: 2 }}>
          {t('center.loadFailed')}
        </Alert>
      ) : items.length === 0 ? (
        <EmptyState title={t('center.empty.title')} description={t('center.empty.description')} />
      ) : (
        <List disablePadding sx={{ overflowY: 'auto', maxHeight: 360, py: 0.5 }}>
          {items.map((item, index) => (
            // A <ul>'s children must be <li>s (axe `list`): the row, with its divider, is one list item.
            <Box key={item.id} component="li" sx={{ listStyle: 'none' }}>
              {/* Full-width, matching the row's own full-bleed hover rectangle (no mx inset) —
                  an inset divider sitting inside a full-width hover highlight is what produced
                  the stepped corner where the two edges didn't line up. */}
              {index > 0 && <Divider />}
              <NotificationItem item={item} onOpen={openItem} dense />
            </Box>
          ))}
        </List>
      )}

      <Divider />
      <Box sx={{ p: 1, textAlign: 'center' }}>
        <Button
          component={RouterLink}
          to="/notifications"
          state={fromPathState(pathname)}
          size="small"
          onClick={onClose}
          sx={{ fontWeight: 600 }}
        >
          {t('center.viewAll')}
        </Button>
      </Box>

      <Snackbar open={markAllRead.isError} autoHideDuration={5000}>
        <Alert severity="error" variant="filled">
          {markAllRead.error ? errorMessage(markAllRead.error, tc('errors.generic')) : null}
        </Alert>
      </Snackbar>
    </Popover>
  )
}
