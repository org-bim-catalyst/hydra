import NotificationsNoneIcon from '@mui/icons-material/NotificationsNone'
import { Box, Chip, List, ListItemButton, ListItemText, Typography } from '@mui/material'
import { useState } from 'react'
import { EmptyState } from '../../../components/EmptyState'
import { useMemoryNotifications } from '../hooks/useMemories'
import { useMarkNotificationRead } from '../hooks/useMemoryMutations'
import { MemoryConflictDialog } from './MemoryConflictDialog'

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}

/**
 * spec.md FR-006a, research.md Decision 11 — the low-noise "Lucy remembered/noticed something"
 * feed; live-pushed via `useMemoryNotificationsHub`, this list is the reconciliation/history
 * view. A `ConflictNeedsConfirmation` notification (User Story 6, FR-016) opens the resolution
 * dialog on click, resolved asynchronously here — never blocking the conversation that surfaced it.
 */
export function MemoryNotificationList() {
  const { data, isLoading } = useMemoryNotifications()
  const markRead = useMarkNotificationRead()
  const [conflictMemoryId, setConflictMemoryId] = useState<string | null>(null)

  const notifications = data?.items ?? []

  if (!isLoading && notifications.length === 0) {
    return <EmptyState icon={<NotificationsNoneIcon fontSize="inherit" />} title="No memory notifications yet" />
  }

  return (
    <>
      <List disablePadding>
        {notifications.map((notification) => (
          <ListItemButton
            key={notification.id}
            onClick={() => {
              if (!notification.readAtUtc) markRead.mutate(notification.id)
              if (notification.eventType === 'ConflictNeedsConfirmation' && notification.memoryId) {
                setConflictMemoryId(notification.memoryId)
              }
            }}
            sx={{ borderRadius: 1, mb: 0.5, bgcolor: notification.readAtUtc ? 'transparent' : 'action.hover' }}
          >
            <ListItemText
              primary={
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  <Typography variant="body2">{notification.message}</Typography>
                  {!notification.readAtUtc && <Chip size="small" color="primary" label="New" />}
                </Box>
              }
              secondary={formatDateTime(notification.createdAtUtc)}
            />
          </ListItemButton>
        ))}
      </List>

      <MemoryConflictDialog open={conflictMemoryId !== null} memoryId={conflictMemoryId} onClose={() => setConflictMemoryId(null)} />
    </>
  )
}
