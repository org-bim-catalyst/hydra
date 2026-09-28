import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import * as notificationsApi from '../api/notificationsApi'
import type { ListNotificationsParams } from '../api/notificationsApi'

export const NOTIFICATIONS_QUERY_KEY = ['notifications']

export const NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY = [...NOTIFICATIONS_QUERY_KEY, 'unread-count']

/** Keyset-paginated notification center list (contracts/notifications-api.md GET /notifications). */
export function useNotifications(params: Omit<ListNotificationsParams, 'cursor'> = {}) {
  return useInfiniteQuery({
    queryKey: [...NOTIFICATIONS_QUERY_KEY, 'list', params],
    queryFn: ({ pageParam }: { pageParam: string | undefined }) => notificationsApi.listNotifications({ ...params, cursor: pageParam }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}

/**
 * The server's `unreadCountChanged` push (useNotificationHub) is the primary update path; this
 * query is only refetched after a reconnect (contracts/notifications-api.md GET
 * /notifications/unread-count) — not polled — so no `refetchInterval` here.
 */
export function useUnreadCount() {
  return useQuery({
    queryKey: NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY,
    queryFn: () => notificationsApi.getUnreadCount(),
  })
}

export function useNotification(id: string | null) {
  return useQuery({
    queryKey: [...NOTIFICATIONS_QUERY_KEY, id],
    queryFn: () => notificationsApi.getNotification(id!),
    enabled: id !== null,
  })
}
