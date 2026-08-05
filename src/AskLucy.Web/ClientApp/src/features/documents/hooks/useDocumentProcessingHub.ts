import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { API_BASE_URL } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'
import { DOCUMENTS_QUERY_KEY } from './useDocuments'

/**
 * Real-time push for one document's processing status (contracts/document-processing-api.md).
 * Never the sole source of truth — useDocumentProcessingStatus's 5s poll is the reconciliation
 * fallback for a missed event or a connection that never establishes (research.md Decision 7),
 * so this hook only invalidates queries on receipt rather than patching state directly. The
 * returned `isLive` flag surfaces connection failures to the UI (constitution's no-silent-
 * failures rule) instead of discarding them — callers render it as a quiet "Live"/"Polling"
 * indicator, not an error toast, since the polling fallback keeps the feature fully functional.
 */
export function useDocumentProcessingHub(documentId: string | null): { isLive: boolean } {
  const queryClient = useQueryClient()
  const connectionRef = useRef<HubConnection | null>(null)
  const [isLive, setIsLive] = useState(false)

  useEffect(() => {
    if (!documentId) {
      return
    }

    const accessToken = useAuthStore.getState().accessToken
    const hubUrl = `${API_BASE_URL.replace(/\/api\/v1$/, '')}/hubs/document-processing`

    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: () => accessToken ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    const invalidate = (eventDocumentId: string) => {
      if (eventDocumentId !== documentId) {
        return
      }
      queryClient.invalidateQueries({ queryKey: [...DOCUMENTS_QUERY_KEY, documentId, 'processing'] })
    }

    connection.on('documentStageChanged', (payload: { documentId: string }) => invalidate(payload.documentId))
    connection.on('documentProcessingCompleted', (payload: { documentId: string }) => invalidate(payload.documentId))
    connection.on('documentProcessingFailed', (payload: { documentId: string }) => invalidate(payload.documentId))
    connection.onreconnected(() => setIsLive(true))
    connection.onreconnecting(() => setIsLive(false))
    connection.onclose(() => setIsLive(false))

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
  }, [documentId, queryClient])

  return { isLive: documentId !== null && isLive }
}
