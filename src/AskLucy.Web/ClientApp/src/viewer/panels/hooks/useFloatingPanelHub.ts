import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useRef } from 'react'
import { API_BASE_URL } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import type { PanelRequest } from '../types/panel'

/**
 * Live push of AI-requested panels (contracts/panel-hub-events.md), mirroring
 * `useAgentExecutionHub.ts`'s connect-once shape. A received `PanelRequested` payload is handed
 * straight to `floatingPanelStore.openPanel`, which owns registry resolution, zod validation,
 * cascade placement, and LRU eviction (data-model.md) — this hook is transport plumbing only.
 */
export function useFloatingPanelHub(): void {
  const connectionRef = useRef<HubConnection | null>(null)

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

    connection.start().catch(() => undefined)
    connectionRef.current = connection

    return () => {
      connection.stop().catch(() => undefined)
      connectionRef.current = null
    }
  }, [])
}
