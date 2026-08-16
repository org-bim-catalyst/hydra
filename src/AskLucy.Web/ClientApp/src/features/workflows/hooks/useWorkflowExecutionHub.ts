import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { API_BASE_URL } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'
import { WORKFLOW_EXECUTIONS_QUERY_KEY } from './useWorkflowExecution'

const EVENT_NAMES = [
  'workflowStarted',
  'nodeStarted',
  'nodeCompleted',
  'nodeFailed',
  'nodeRetrying',
  'approvalRequested',
  'approvalGranted',
  'approvalRejected',
  'workflowPaused',
  'workflowResumed',
  'workflowCompleted',
  'workflowFailed',
  'workflowCancelled',
  'usageUpdated',
] as const

/**
 * Live push for one execution's progress (spec.md User Story 6, contracts/workflow-execution-events.md).
 * The server-side group is per-user, not per-execution — one connection carries every one of the
 * caller's concurrent executions' events, so this hook filters to `executionId` client-side and
 * invalidates that execution's query cache on a match, letting the existing REST poll
 * (`useWorkflowExecution`) do the actual refetch. Never the sole source of truth — a missed push (a
 * reconnect gap) is caught by that same poll's next 2s tick. Constitution §2.VIII no-silent-failure:
 * a failed connection surfaces as `isLive: false`, which callers render as a visible "reconnecting"
 * state rather than silently degrading with no indication.
 */
export function useWorkflowExecutionHub(executionId: string | null): { isLive: boolean } {
  const queryClient = useQueryClient()
  const connectionRef = useRef<HubConnection | null>(null)
  const [isLive, setIsLive] = useState(false)

  useEffect(() => {
    if (!executionId) {
      return
    }

    const accessToken = useAuthStore.getState().accessToken
    const hubUrl = `${API_BASE_URL.replace(/\/api\/v1$/, '')}/hubs/workflow-execution`

    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, { accessTokenFactory: () => accessToken ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    const invalidate = (payload: { executionId: string }) => {
      if (payload.executionId !== executionId) {
        return
      }
      queryClient.invalidateQueries({ queryKey: [...WORKFLOW_EXECUTIONS_QUERY_KEY, executionId] })
    }

    for (const eventName of EVENT_NAMES) {
      connection.on(eventName, invalidate)
    }
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
  }, [executionId, queryClient])

  return { isLive: executionId !== null && isLive }
}
