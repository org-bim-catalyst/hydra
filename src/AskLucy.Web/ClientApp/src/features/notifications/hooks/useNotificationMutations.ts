import { useMutation, useQueryClient } from '@tanstack/react-query'
import * as notificationsApi from '../api/notificationsApi'
import type { NotificationCategory } from '../api/notificationsApi'
import { NOTIFICATIONS_QUERY_KEY, NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY } from './useNotifications'

/**
 * T067 deviation: hooks can't render a `<Snackbar>` themselves, so — mirroring
 * `RemoveCustomModelButton`'s established error-surfacing convention — each mutation here exposes
 * TanStack Query's own `error`/`isError` (via `errorMessage()`, see `errorMessage.ts`), and the
 * component that calls the hook (`NotificationItem`, `NotificationPopover`, `NotificationsPage`;
 * T069/T071/T072) is the one that renders the toast from it. No pushed error is ever left
 * unsurfaced (constitution's no-silent-failures rule): every one of these mutations reaches a
 * caller-visible `error` on failure.
 */
function useInvalidateNotifications() {
  const queryClient = useQueryClient()
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_QUERY_KEY }),
      queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY }),
    ])
}

export function useMarkNotificationRead() {
  const invalidate = useInvalidateNotifications()
  return useMutation({
    mutationFn: (id: string) => notificationsApi.markNotificationRead(id),
    onSuccess: invalidate,
  })
}

export function useMarkAllNotificationsRead() {
  const invalidate = useInvalidateNotifications()
  return useMutation({
    mutationFn: (category?: NotificationCategory) => notificationsApi.markAllNotificationsRead(category),
    onSuccess: invalidate,
  })
}

export function useDeleteNotification() {
  const invalidate = useInvalidateNotifications()
  return useMutation({
    mutationFn: (id: string) => notificationsApi.deleteNotification(id),
    onSuccess: invalidate,
  })
}
