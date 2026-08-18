import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { API_BASE_URL } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'
import { MEMORIES_QUERY_KEY } from './useMemories'
import type { MemoryNotification } from '../api/memoryApi'

/**
 * SignalR listener for `memoryNotificationCreated` on `/hubs/memory` (spec.md FR-006a,
 * research.md Decision 11) — mirrors `useNotificationHub` (documents feature) exactly. The
 * `useMemoryNotifications` poll query (`useMemories.ts`) is the reconciliation fallback for
 * anything missed while disconnected; this hook only surfaces the latest event and invalidates
 * that query on receipt.
 */
export function useMemoryNotificationsHub(): { latest: MemoryNotification | null; dismiss: () => void } {
  const queryClient = useQueryClient()
  const connectionRef = useRef<HubConnection | null>(null)
  const [latest, setLatest] = useState<MemoryNotification | null>(null)

  useEffect(() => {
    const accessToken = useAuthStore.getState().accessToken
    if (!accessToken) {
      return
    }

    const hubUrl = `${API_BASE_URL.replace(/\/api\/v1$/, '')}/hubs/memory`
    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: () => accessToken })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('memoryNotificationCreated', (payload: MemoryNotification) => {
      setLatest(payload)
      queryClient.invalidateQueries({ queryKey: [...MEMORIES_QUERY_KEY, 'notifications'] })
      queryClient.invalidateQueries({ queryKey: [...MEMORIES_QUERY_KEY, 'pending'] })
    })

    connection.start().catch(() => undefined) // The polled notifications query is the fallback if the connection never establishes.
    connectionRef.current = connection

    return () => {
      connection.stop().catch(() => undefined)
      connectionRef.current = null
    }
  }, [queryClient])

  return { latest, dismiss: () => setLatest(null) }
}
