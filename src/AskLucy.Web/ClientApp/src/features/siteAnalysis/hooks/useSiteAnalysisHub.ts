import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState } from 'react'
import { API_BASE_URL } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'
import type { SiteAnalysisCompletedPayload, SiteAnalysisResultReceivedPayload } from '../api/siteAnalysisApi'
import { useSiteAnalysisNoticeStore } from '../store/siteAnalysisNoticeStore'

/**
 * Live push of site-analysis chat notices (contracts/site-analysis-hub-events.md), mirroring
 * `useFloatingPanelHub`'s connect-once shape exactly — chat has no SignalR hub of its own
 * (research.md D5), so this exists solely to tell an open chat "a new message just landed."
 *
 * Both `SiteAnalysisResultReceived` and `SiteAnalysisCompleted` are backed by a real, persisted
 * assistant message (`SiteAnalysisResultRelay` — contracts/result-relay.md § Delivery on
 * success), so this hook does not render anything itself. It hands each notice to
 * `useSiteAnalysisNoticeStore`, which the open conversation view appends live (a refetch alone
 * never reaches a view the user has already sent in — see that store's doc), and it also
 * invalidates the conversation's `['chats', userChatId, 'messages']` query so any other cached
 * copy of that history is current. The
 * corresponding floating panel arrives separately via the existing, already-mounted
 * `useFloatingPanelHub` — nothing panel-specific belongs here.
 *
 * `isLive` mirrors `useFloatingPanelHub`'s own reasoning: there is no REST-poll fallback for a
 * live notice, so a caller rendering `isLive: false` is the only signal a user gets that new
 * findings won't appear without a manual refresh.
 */
export function useSiteAnalysisHub(): { isLive: boolean } {
  const connectionRef = useRef<HubConnection | null>(null)
  const [isLive, setIsLive] = useState(false)
  const queryClient = useQueryClient()
  const pushNotice = useSiteAnalysisNoticeStore((state) => state.push)
  // A boolean, not the token itself: the effect must (re)connect when a session appears — the
  // token is often not in the store yet on first mount while the cookie session restores, and
  // reading it only once made this hook silently never connect — but must NOT tear the
  // connection down on every 15-minute token refresh.
  const isAuthenticated = useAuthStore((state) => state.accessToken !== null)

  useEffect(() => {
    if (!isAuthenticated) {
      return
    }

    const hubUrl = `${API_BASE_URL.replace(/\/api\/v1$/, '')}/hubs/site-analysis`

    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    const invalidateMessages = (userChatId: string) => {
      void queryClient.invalidateQueries({ queryKey: ['chats', userChatId, 'messages'] })
    }

    connection.on('SiteAnalysisResultReceived', (payload: SiteAnalysisResultReceivedPayload) => {
      pushNotice({ id: `result:${payload.resultId}`, userChatId: payload.userChatId, text: payload.noticeText })
      invalidateMessages(payload.userChatId)
    })

    connection.on('SiteAnalysisCompleted', (payload: SiteAnalysisCompletedPayload) => {
      if (payload.noticeText !== null) {
        pushNotice({ id: `completed:${payload.analysisId}`, userChatId: payload.userChatId, text: payload.noticeText })
        invalidateMessages(payload.userChatId)
      }
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
  }, [queryClient, pushNotice, isAuthenticated])

  return { isLive }
}
