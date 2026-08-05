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
 */
export function useNotificationHub(): { latest: IncomingNotification | null; dismiss: () => void } {
  const queryClient = useQueryClient()
  const connectionRef = useRef<HubConnection | null>(null)
  const [latest, setLatest] = useState<IncomingNotification | null>(null)

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

    connection.start().catch(() => undefined) // The 5s-polled inbox query is the fallback if the connection never establishes.
    connectionRef.current = connection

    return () => {
      connection.stop().catch(() => undefined)
      connectionRef.current = null
    }
  }, [queryClient])

  return { latest, dismiss: () => setLatest(null) }
}
