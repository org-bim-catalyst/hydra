import { Alert, Box, Button, Drawer, MenuItem, Select, Snackbar, Stack, ToggleButton, ToggleButtonGroup } from '@mui/material'
import type { SelectChangeEvent } from '@mui/material'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { AppShell } from '../../../components/AppShell'
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

/** T072 — category/state filters, infinite scroll, a details drawer driven by `/notifications/:id`, and mark-all-read. */
export function NotificationsPage() {
  const navigate = useNavigate()
  const { id: openId } = useParams<{ id: string }>()

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
      fillViewport
      actions={
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          <ToggleButtonGroup size="small" value={state} exclusive onChange={(_, next: NotificationState | null) => next && setState(next)}>
            <ToggleButton value="all">All</ToggleButton>
            <ToggleButton value="unread">Unread</ToggleButton>
            <ToggleButton value="read">Read</ToggleButton>
          </ToggleButtonGroup>
          <Select<NotificationCategory[]>
            multiple
            size="small"
            displayEmpty
            aria-label="Filter by category"
            value={categories}
            onChange={(e: SelectChangeEvent<NotificationCategory[]>) =>
              setCategories(typeof e.target.value === 'string' ? [] : e.target.value)
            }
            renderValue={(selected) => (selected.length === 0 ? 'All categories' : selected.join(', '))}
            sx={{ minWidth: 200 }}
          >
            {CATEGORIES.map((category) => (
              <MenuItem key={category} value={category}>
                {category}
              </MenuItem>
            ))}
          </Select>
          <Button size="small" onClick={() => markAllRead.mutate(undefined)} disabled={markAllRead.isPending || items.length === 0}>
            Mark all read
          </Button>
        </Stack>
      }
    >
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
