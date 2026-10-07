import { Alert, Box, Button, Drawer, MenuItem, Snackbar, Stack, TextField, ToggleButton, ToggleButtonGroup, useTheme } from '@mui/material'
import { useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import { useT } from '../../../i18n/useT'
import { fromPath } from '../../../routes/viewLandingState'
import { errorMessage } from '../api/errorMessage'
import type { NotificationCategory, NotificationItem as NotificationItemDto, NotificationState } from '../api/notificationsApi'
import { NotificationDetails } from '../components/NotificationDetails'
import { NotificationList } from '../components/NotificationList'
import { useMarkAllNotificationsRead } from '../hooks/useNotificationMutations'
import { useNotification, useNotifications } from '../hooks/useNotifications'

const CATEGORIES: NotificationCategory[] = [
  'Security',
  'Account',
  'Agent',
  'Workflow',
  'Document',
  'KnowledgeBase',
  'Memory',
  'System',
  'Billing',
  'Conversation',
]

/** T072 — category/state filters, infinite scroll, a details drawer driven by `/notifications/:id`, and mark-all-read. Styled to match the Memory Center (title/subtitle header, labeled filter row, item count line). */
export function NotificationsPage() {
  // The surface wraps the whole page, shell included, because the title and subtitle are part of the localized
  // content; the rest of the app stays English and left-to-right.
  return (
    <LocalizedSurface scope="subtree">
      <NotificationsPageContent />
    </LocalizedSurface>
  )
}

function NotificationsPageContent() {
  const t = useT('notifications')
  const tc = useT('common')
  const drawerEdge = useTheme().direction === 'rtl' ? 'left' : 'right'
  const navigate = useNavigate()
  const { id: openId } = useParams<{ id: string }>()
  const { state: locationState } = useLocation()

  const [state, setState] = useState<NotificationState>('all')
  const [categories, setCategories] = useState<NotificationCategory[]>([])

  const { data, isLoading, isError, isFetchingNextPage, hasNextPage, fetchNextPage, refetch } = useNotifications({
    state,
    category: categories,
  })
  const markAllRead = useMarkAllNotificationsRead()
  const detail = useNotification(openId ?? null)

  const items = data?.pages.flatMap((page) => page.items) ?? []

  const openItem = (item: NotificationItemDto) => navigate(`/notifications/${item.id}`)
  const closeDrawer = () => navigate('/notifications')

  return (
    <AppShell
      title={t('center.title')}
      subtitle={t('center.subtitle')}
      homeTo={fromPath(locationState) ?? undefined}
      fillViewport
    >
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ mb: 3, alignItems: { sm: 'center' } }}>
        <ToggleButtonGroup size="small" value={state} exclusive onChange={(_, next: NotificationState | null) => next && setState(next)}>
          <ToggleButton value="all">{t('center.filters.all')}</ToggleButton>
          <ToggleButton value="unread">{t('center.filters.unread')}</ToggleButton>
          <ToggleButton value="read">{t('center.filters.read')}</ToggleButton>
        </ToggleButtonGroup>

        <TextField
          select
          size="small"
          label={t('center.filters.category')}
          aria-label={t('center.filters.categoryAria')}
          value={categories}
          onChange={(e) => setCategories(typeof e.target.value === 'string' ? [] : (e.target.value as unknown as NotificationCategory[]))}
          slotProps={{
            select: {
              multiple: true,
              renderValue: (selected) =>
                (selected as NotificationCategory[]).length === 0
                  ? t('center.filters.allCategories')
                  : (selected as NotificationCategory[]).map((category) => t(`categories.${category}`)).join(t('center.filters.listSeparator')),
            },
          }}
          sx={{ minWidth: 200 }}
        >
          {CATEGORIES.map((category) => (
            <MenuItem key={category} value={category}>
              {t(`categories.${category}`)}
            </MenuItem>
          ))}
        </TextField>

        <Box sx={{ flex: 1 }} />

        <Button size="small" onClick={() => markAllRead.mutate(undefined)} disabled={markAllRead.isPending || items.length === 0}>
          {t('center.markAllRead')}
        </Button>
      </Stack>

      <Box sx={{ flex: 1, minHeight: 0, display: 'flex' }}>
        <NotificationList
          items={items}
          isLoading={isLoading}
          isError={isError}
          onRetry={() => void refetch()}
          isFetchingNextPage={isFetchingNextPage}
          hasNextPage={hasNextPage}
          onFetchNextPage={() => void fetchNextPage()}
          onOpen={openItem}
        />
      </Box>

      <Drawer anchor={drawerEdge} open={openId !== undefined} onClose={closeDrawer} slotProps={{ paper: { sx: { width: 420 } } }}>
        <NotificationDetails
          notification={detail.data}
          isLoading={detail.isLoading}
          isError={detail.isError}
          onDeleted={closeDrawer}
        />
      </Drawer>

      <Snackbar open={markAllRead.isError} autoHideDuration={5000}>
        <Alert severity="error" variant="filled">
          {markAllRead.error ? errorMessage(markAllRead.error, tc('errors.generic')) : null}
        </Alert>
      </Snackbar>
    </AppShell>
  )
}
