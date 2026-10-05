import { Alert, Box, Button, Drawer, MenuItem, Snackbar, Stack, TextField, ToggleButton, ToggleButtonGroup } from '@mui/material'
import { useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router'
import { AppShell } from '../../../components/AppShell'
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
      title="Notifications"
      subtitle="Everything Lucy has told you — review, filter, or act on any of it."
      homeTo={fromPath(locationState) ?? undefined}
      fillViewport
    >
      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ mb: 3, alignItems: { sm: 'center' } }}>
        <ToggleButtonGroup size="small" value={state} exclusive onChange={(_, next: NotificationState | null) => next && setState(next)}>
          <ToggleButton value="all">All</ToggleButton>
          <ToggleButton value="unread">Unread</ToggleButton>
          <ToggleButton value="read">Read</ToggleButton>
        </ToggleButtonGroup>

        <TextField
          select
          size="small"
          label="Category"
          aria-label="Filter by category"
          value={categories}
          onChange={(e) => setCategories(typeof e.target.value === 'string' ? [] : (e.target.value as unknown as NotificationCategory[]))}
          slotProps={{
            select: {
              multiple: true,
              renderValue: (selected) => ((selected as NotificationCategory[]).length === 0 ? 'All categories' : (selected as NotificationCategory[]).join(', ')),
            },
          }}
          sx={{ minWidth: 200 }}
        >
          {CATEGORIES.map((category) => (
            <MenuItem key={category} value={category}>
              {category}
            </MenuItem>
          ))}
        </TextField>

        <Box sx={{ flex: 1 }} />

        <Button size="small" onClick={() => markAllRead.mutate(undefined)} disabled={markAllRead.isPending || items.length === 0}>
          Mark all read
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

      <Drawer anchor="right" open={openId !== undefined} onClose={closeDrawer} slotProps={{ paper: { sx: { width: 420 } } }}>
        <NotificationDetails
          notification={detail.data}
          isLoading={detail.isLoading}
          isError={detail.isError}
          onDeleted={closeDrawer}
        />
      </Drawer>

      <Snackbar open={markAllRead.isError} autoHideDuration={5000}>
        <Alert severity="error" variant="filled">
          {markAllRead.error ? errorMessage(markAllRead.error) : null}
        </Alert>
      </Snackbar>
    </AppShell>
  )
}
