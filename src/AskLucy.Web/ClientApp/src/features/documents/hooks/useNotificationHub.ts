import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { API_BASE_URL } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'
import { DOCUMENTS_QUERY_KEY } from './useDocuments'
import type { DocumentNotificationEventType } from '../api/documentsApi'

export interface IncomingNotification {
  id: string
  documentId: string | null
  eventType: DocumentNotificationEventType
  message: string
  createdAtUtc: string
}

/**
 * Workspace-level (not per-document) SignalR listener for `notificationCreated` (FR-047,
 * contracts/document-processing-api.md) — one connection for the whole session, established
 * once `DocumentWorkspacePage` mounts, unlike `useDocumentProcessingHub` which only connects
 * while a specific document's detail panel is open. The inbox query (`useNotifications`) is the
 * reconciliation fallback for anything missed while disconnected (same principle as research.md
 * Decision 7) — this hook only surfaces a toast and invalidates the query on receipt.
 *
 * The returned `isLive` flag surfaces connection failures to the caller instead of discarding
 * them — matches `useDocumentProcessingHub`/`useAgentExecutionHub`/`useWorkflowExecutionHub`'s
 * established pattern (specs/029-fix-chat-widget-bugs FR-010, analysis finding C1: this hook
 * previously called `connection.start().catch(() => undefined)`, silently discarding a failed
 * connection, the exact pattern constitution §2.VIII forbids). The polled inbox query remains
 * the reconciliation fallback, so `isLive: false` is a quiet "still working, just polling"
 * signal, not an error.
 */
export function useNotificationHub(): { latest: IncomingNotification | null; dismiss: () => void; isLive: boolean } {
  const queryClient = useQueryClient()
  const connectionRef = useRef<HubConnection | null>(null)
  const [latest, setLatest] = useState<IncomingNotification | null>(null)
  const [isLive, setIsLive] = useState(false)

  useEffect(() => {
    const accessToken = useAuthStore.getState().accessToken
    if (!accessToken) {
      return
    }

    const hubUrl = `${API_BASE_URL.replace(/\/api\/v1$/, '')}/hubs/document-processing`
    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: () => accessToken })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('notificationCreated', (payload: IncomingNotification) => {
      setLatest(payload)
      queryClient.invalidateQueries({ queryKey: [...DOCUMENTS_QUERY_KEY, 'notifications'] })
    })

    connection.onreconnected(() => setIsLive(true))
    connection.onreconnecting(() => setIsLive(false))
    connection.onclose(() => setIsLive(false))

    // The 5s-polled inbox query is the fallback if the connection never establishes.
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
