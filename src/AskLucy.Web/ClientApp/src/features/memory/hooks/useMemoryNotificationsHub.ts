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
 *
 * The returned `isLive` flag surfaces connection failures to the caller instead of discarding
 * them — matches `useDocumentProcessingHub`/`useAgentExecutionHub`/`useWorkflowExecutionHub`'s
 * established pattern (specs/029-fix-chat-widget-bugs FR-010, analysis finding C1: this hook
 * previously called `connection.start().catch(() => undefined)`, silently discarding a failed
 * connection, the exact pattern constitution §2.VIII forbids). The polled notifications query
 * remains the reconciliation fallback, so `isLive: false` is a quiet "still working, just
 * polling" signal, not an error.
 */
export function useMemoryNotificationsHub(): { latest: MemoryNotification | null; dismiss: () => void; isLive: boolean } {
  const queryClient = useQueryClient()
  const connectionRef = useRef<HubConnection | null>(null)
  const [latest, setLatest] = useState<MemoryNotification | null>(null)
  const [isLive, setIsLive] = useState(false)

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

    connection.onreconnected(() => setIsLive(true))
    connection.onreconnecting(() => setIsLive(false))
    connection.onclose(() => setIsLive(false))

    // The polled notifications query is the fallback if the connection never establishes.
    connection.start().then(
      () => setIsLive(true),
      () => setIsLive(false),
    )
    connectionRef.current = connection

    return () => {
      connection.stop().catch(() => undefined)
      connectionRef.current = null
      setIsLive(false)
    }
  }, [queryClient])

  return { latest, dismiss: () => setLatest(null), isLive }
}
