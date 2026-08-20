import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { API_BASE_URL } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import type { PanelRequest } from '../types/panel'

/**
 * Live push of AI-requested panels (contracts/panel-hub-events.md), mirroring
 * `useAgentExecutionHub.ts`'s connect-once shape. A received `PanelRequested` payload is handed
 * straight to `floatingPanelStore.openPanel`, which owns registry resolution, zod validation,
 * cascade placement, and LRU eviction (data-model.md) — this hook is transport plumbing only.
 *
 * The returned `isLive` flag surfaces connection failures to the caller instead of discarding
 * them — matches the established pattern in `useDocumentProcessingHub`/`useAgentExecutionHub`/
 * `useWorkflowExecutionHub` (specs/029-fix-chat-widget-bugs FR-010, analysis finding C1: this
 * hook previously called `connection.start().catch(() => undefined)`, silently discarding a
 * failed connection with no trace, the exact pattern constitution §2.VIII forbids). Unlike
 * those three hooks, there's no REST-poll fallback for AI-requested panels, so a caller
 * rendering `isLive: false` is the only signal a user gets that panels won't arrive live.
 */
export function useFloatingPanelHub(): { isLive: boolean } {
  const connectionRef = useRef<HubConnection | null>(null)
  const [isLive, setIsLive] = useState(false)

  useEffect(() => {
    const accessToken = useAuthStore.getState().accessToken
    if (!accessToken) {
      return
    }

    const hubUrl = `${API_BASE_URL.replace(/\/api\/v1$/, '')}/hubs/panels`

    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: () => accessToken })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('PanelRequested', (payload: PanelRequest) => {
      useFloatingPanelStore.getState().openPanel(payload)
    })

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
  }, [])

  return { isLive }
}
