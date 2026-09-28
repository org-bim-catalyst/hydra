import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { type InfiniteData, useQueryClient } from '@tanstack/react-query'
import { API_BASE_URL } from '../../../api/httpClient'
import { keepHubConnected } from '../../../api/hubConnection'
import { NOTIFICATIONS_QUERY_KEY, NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY } from './useNotifications'
import type { NotificationItem, NotificationPage, UnreadCount } from '../api/notificationsApi'

interface NotificationCreatedPayload extends NotificationItem {
  unreadCount: number
}

interface NotificationUpdatedPayload {
  id: string
  change: 'Read' | 'Deleted' | 'Expired'
  unreadCount: number
}

interface UnreadCountChangedPayload {
  unreadCount: number
}

const LIST_QUERY_KEY = [...NOTIFICATIONS_QUERY_KEY, 'list']

/** Contracts/notifications-api.md: `High`/`Critical` created events also raise a toast + a11y announcement. */
export interface NotificationAnnouncement {
  id: string
  title: string
  message: string
  priority: NotificationItem['priority']
}

/**
 * contracts/notification-hub.md — `/hubs/notifications`, server → client only, three events.
 * Auth is the same cookie-delivered token every other hub in this codebase uses (no
 * `accessTokenFactory` anywhere — see `signalr_hub_frozen_token_bug`); `keepHubConnected` is what
 * "carries the token factory" per T068's wording, not a literal per-call token callback.
 */
export function useNotificationHub(): {
  isLive: boolean
  latestAnnouncement: NotificationAnnouncement | null
  dismissAnnouncement: () => void
} {
  const queryClient = useQueryClient()
  const connectionRef = useRef<HubConnection | null>(null)
  const [isLive, setIsLive] = useState(false)
  const [latestAnnouncement, setLatestAnnouncement] = useState<NotificationAnnouncement | null>(null)

  useEffect(() => {
    const hubUrl = `${API_BASE_URL.replace(/\/api\/v1$/, '')}/hubs/notifications`
    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('notificationCreated', (payload: NotificationCreatedPayload) => {
      const { unreadCount, ...item } = payload

      queryClient.setQueriesData<InfiniteData<NotificationPage>>({ queryKey: LIST_QUERY_KEY }, (data) => {
        if (!data) return data
        const [firstPage, ...restPages] = data.pages
        return { ...data, pages: [{ ...firstPage, items: [item, ...firstPage.items] }, ...restPages] }
      })
      queryClient.setQueryData<UnreadCount>(NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY, { count: unreadCount })

      if (item.priority === 'High' || item.priority === 'Critical') {
        setLatestAnnouncement({ id: item.id, title: item.title, message: item.message, priority: item.priority })
      }
    })

    connection.on('notificationUpdated', ({ id, change, unreadCount }: NotificationUpdatedPayload) => {
      queryClient.setQueriesData<InfiniteData<NotificationPage>>({ queryKey: LIST_QUERY_KEY }, (data) => {
        if (!data) return data
        return {
          ...data,
          pages: data.pages.map((page) => ({
            ...page,
            items:
              change === 'Deleted' || change === 'Expired'
                ? page.items.filter((item) => item.id !== id)
                : page.items.map((item) => (item.id === id ? { ...item, readAtUtc: new Date().toISOString() } : item)),
          })),
        }
      })
      queryClient.setQueryData<UnreadCount>(NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY, { count: unreadCount })
      void queryClient.invalidateQueries({ queryKey: [...NOTIFICATIONS_QUERY_KEY, id] })
    })

    connection.on('unreadCountChanged', ({ unreadCount }: UnreadCountChangedPayload) => {
      queryClient.setQueryData<UnreadCount>(NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY, { count: unreadCount })
    })

    // Pushes are best-effort (notification-hub.md "Failure semantics") — the notification center
    // is the source of truth, so a reconnect must refetch rather than trust the cache survived
    // the gap uncorrupted (edge case: offline users lose nothing).
    const onReconnected = () => {
      void queryClient.invalidateQueries({ queryKey: LIST_QUERY_KEY })
      void queryClient.invalidateQueries({ queryKey: NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY })
    }

    const disconnect = keepHubConnected(connection, setIsLive, onReconnected)
    connectionRef.current = connection

    return () => {
      disconnect()
      connectionRef.current = null
      setIsLive(false)
    }
  }, [queryClient])

  return { isLive, latestAnnouncement, dismissAnnouncement: () => setLatestAnnouncement(null) }
}
