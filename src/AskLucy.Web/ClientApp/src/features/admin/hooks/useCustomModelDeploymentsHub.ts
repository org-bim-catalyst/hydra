import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { API_BASE_URL } from '../../../api/httpClient'
import { keepHubConnected } from '../../../api/hubConnection'
import { useAuthStore } from '../../../store/authStore'
import {
  CUSTOM_MODELS_QUERY_KEYS,
  type CustomModelDeploymentProgress,
  type CustomModelDeploymentStateChanged,
  type CustomModelSummary,
  type Paged,
  type TransferPhase,
} from '../api/adminCustomModelsApi'

const LIST_KEY = [...CUSTOM_MODELS_QUERY_KEYS.all, 'list'] as const

/**
 * Applies `update` to the row with `id` in every cached list page. Returns false when no cached
 * page holds that row, so the caller can refetch instead of dropping the event.
 */
function updateCachedRow(
  queryClient: QueryClient,
  id: string,
  update: (row: CustomModelSummary) => CustomModelSummary | null,
): boolean {
  let found = false
  queryClient.setQueriesData<Paged<CustomModelSummary>>({ queryKey: LIST_KEY }, (page) => {
    if (!page || !page.items.some((row) => row.id === id)) {
      return page
    }
    found = true
    const items = page.items.flatMap((row) => {
      if (row.id !== id) return [row]
      const next = update(row)
      return next ? [next] : []
    })
    return { ...page, items, totalCount: page.totalCount - (page.items.length - items.length) }
  })
  return found
}

/**
 * Live deployment progress (contracts/custom-model-deployment-hub.md), following
 * `useSiteAnalysisHub`'s connect-once shape. Events patch the cached list rather than refetching
 * it, since progress arrives every 500 ms; anything the cache can't place is refetched over REST,
 * and so is everything after a reconnect, because events sent while disconnected are gone.
 *
 * `connectionLost` differs from `!isLive` only while the first connect is pending: the section
 * shows its "reconnecting" banner on `connectionLost`, so it doesn't flash on every page load.
 * `phaseById` holds each model's latest transfer phase, which the REST summary doesn't carry.
 */
export function useCustomModelDeploymentsHub(): {
  isLive: boolean
  connectionLost: boolean
  phaseById: Record<string, TransferPhase>
} {
  const [isLive, setIsLive] = useState(false)
  const [connectionLost, setConnectionLost] = useState(false)
  const [phaseById, setPhaseById] = useState<Record<string, TransferPhase>>({})
  const queryClient = useQueryClient()
  // A boolean, not the token: reconnect when a session appears, but not on every token refresh.
  const isAuthenticated = useAuthStore((state) => state.accessToken !== null)

  useEffect(() => {
    if (!isAuthenticated) {
      return
    }

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_BASE_URL.replace(/\/api\/v1$/, '')}/hubs/custom-model-deployments`)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    const refetchList = () => void queryClient.invalidateQueries({ queryKey: LIST_KEY })

    const setLive = (live: boolean) => {
      setIsLive(live)
      setConnectionLost(!live)
    }

    connection.on('CustomModelDeploymentProgress', (event: CustomModelDeploymentProgress) => {
      setPhaseById((phases) =>
        phases[event.customModelId] === event.phase ? phases : { ...phases, [event.customModelId]: event.phase },
      )
      const found = updateCachedRow(queryClient, event.customModelId, (row) => ({
        ...row,
        deploymentState: event.deploymentState,
        transferredBytes: event.transferredBytes,
        totalBytes: event.totalBytes,
        completedFileCount: event.completedFileCount,
        totalFileCount: event.totalFileCount,
        currentFilePath: event.currentFilePath,
        currentFileBytes: event.currentFileBytes,
        currentFileTotalBytes: event.currentFileTotalBytes,
        overwrittenFileCount: row.overwrittenFileCount + (event.overwrote ? 1 : 0),
      }))
      if (!found) refetchList()
    })

    connection.on('CustomModelDeploymentStateChanged', (event: CustomModelDeploymentStateChanged) => {
      const { removed, ...summary } = event
      const found = updateCachedRow(queryClient, event.id, () => (removed ? null : summary))
      if (!found && !removed) refetchList()
      void queryClient.invalidateQueries({ queryKey: [...CUSTOM_MODELS_QUERY_KEYS.all, 'detail', event.id] })
    })

    const disconnect = keepHubConnected(connection, setLive, () => {
      void queryClient.invalidateQueries({ queryKey: CUSTOM_MODELS_QUERY_KEYS.all })
    })

    return () => {
      disconnect()
      setIsLive(false)
      setConnectionLost(false)
    }
  }, [queryClient, isAuthenticated])

  return { isLive, connectionLost, phaseById }
}
