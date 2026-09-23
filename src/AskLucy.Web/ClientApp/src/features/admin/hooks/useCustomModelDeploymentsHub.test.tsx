import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../../../store/authStore'
import type {
  CustomModelDeploymentProgress,
  CustomModelSummary,
  Paged,
} from '../api/adminCustomModelsApi'
import { CUSTOM_MODELS_QUERY_KEYS } from '../api/adminCustomModelsApi'
import { customModel } from '../components/customModels/customModelFixtures'
import { useCustomModelDeploymentsHub } from './useCustomModelDeploymentsHub'

const handlers: Record<string, (payload: unknown) => void> = {}
let startResult: Promise<void> = Promise.resolve()
let builds = 0
let hubUrl: string | undefined
let onreconnected: (() => void) | undefined
let onreconnecting: (() => void) | undefined
let onclose: (() => void) | undefined

vi.mock('@microsoft/signalr', () => {
  class MockHubConnectionBuilder {
    withUrl(url: string) {
      hubUrl = url
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      builds++
      return {
        on: (event: string, handler: (payload: unknown) => void) => {
          handlers[event] = handler
        },
        onreconnected: (cb: () => void) => {
          onreconnected = cb
        },
        onreconnecting: (cb: () => void) => {
          onreconnecting = cb
        },
        onclose: (cb: () => void) => {
          onclose = cb
        },
        start: () => startResult,
        stop: () => Promise.resolve(),
      }
    }
  }
  return { HubConnectionBuilder: MockHubConnectionBuilder, LogLevel: { Warning: 2 } }
})

const LIST_KEY = CUSTOM_MODELS_QUERY_KEYS.list(1, 50)

let queryClient: QueryClient

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
}

function seedList(items: CustomModelSummary[]) {
  queryClient.setQueryData<Paged<CustomModelSummary>>(LIST_KEY, { items, page: 1, pageSize: 50, totalCount: items.length })
}

const cachedRows = () => queryClient.getQueryData<Paged<CustomModelSummary>>(LIST_KEY)?.items ?? []

function progress(overrides: Partial<CustomModelDeploymentProgress> = {}): CustomModelDeploymentProgress {
  return {
    customModelId: 'model-1',
    deploymentState: 'Transferring',
    transferredBytes: 512,
    totalBytes: 2048,
    completedFileCount: 1,
    totalFileCount: 4,
    currentFilePath: 'onnx/text_encoder.onnx',
    currentFileBytes: 100,
    currentFileTotalBytes: 400,
    phase: 'Uploading',
    overwrote: null,
    sentAtUtc: '2026-09-23T10:01:00Z',
    ...overrides,
  }
}

async function renderConnected() {
  const hook = renderHook(() => useCustomModelDeploymentsHub(), { wrapper })
  await act(async () => {
    await Promise.resolve()
  })
  return hook
}

describe('useCustomModelDeploymentsHub', () => {
  beforeEach(() => {
    queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    useAuthStore.setState({ accessToken: 'test-token', userId: 'u1' })
    startResult = Promise.resolve()
    builds = 0
    hubUrl = undefined
    onreconnected = undefined
    onreconnecting = undefined
    onclose = undefined
    for (const key of Object.keys(handlers)) delete handlers[key]
  })

  it('connects to the deployments hub only once the user is authenticated', async () => {
    useAuthStore.setState({ accessToken: null })
    const { result, rerender } = renderHook(() => useCustomModelDeploymentsHub(), { wrapper })

    expect(builds).toBe(0)
    expect(result.current.isLive).toBe(false)

    act(() => useAuthStore.setState({ accessToken: 'test-token' }))
    rerender()
    await act(async () => {
      await Promise.resolve()
    })

    expect(builds).toBe(1)
    expect(hubUrl).toMatch(/\/hubs\/custom-model-deployments$/)
    expect(result.current.isLive).toBe(true)
  })

  it('patches the cached row in place from a progress event', async () => {
    seedList([customModel({ deploymentState: 'Queued', transferredBytes: 0, totalBytes: null, canCancel: true })])
    const { result } = await renderConnected()

    act(() => handlers.CustomModelDeploymentProgress(progress()))

    const [row] = cachedRows()
    expect(row).toMatchObject({
      deploymentState: 'Transferring',
      transferredBytes: 512,
      totalBytes: 2048,
      completedFileCount: 1,
      totalFileCount: 4,
      currentFilePath: 'onnx/text_encoder.onnx',
      currentFileBytes: 100,
      currentFileTotalBytes: 400,
      canCancel: true,
    })
    expect(result.current.phaseById['model-1']).toBe('Uploading')
  })

  it('counts an overwrite reported by a progress event', async () => {
    seedList([customModel({ deploymentState: 'Transferring', overwrittenFileCount: 2 })])
    await renderConnected()

    act(() =>
      handlers.CustomModelDeploymentProgress(progress({ overwrote: { relativePath: 'config.json', previousSizeBytes: 10 } })),
    )

    expect(cachedRows()[0].overwrittenFileCount).toBe(3)
  })

  it('replaces the cached row from a state-changed event', async () => {
    seedList([customModel({ deploymentState: 'Transferring', canCancel: true }), customModel({ id: 'model-2', name: 'other' })])
    await renderConnected()

    act(() =>
      handlers.CustomModelDeploymentStateChanged(
        customModel({ deploymentState: 'Failed', canCancel: false, failureReason: 'The deployment target could not be reached.' }),
      ),
    )

    expect(cachedRows()).toHaveLength(2)
    expect(cachedRows()[0]).toMatchObject({ deploymentState: 'Failed', canCancel: false })
    expect(cachedRows()[1].name).toBe('other')
  })

  it('drops the row when the state-changed event says it was removed', async () => {
    seedList([customModel(), customModel({ id: 'model-2' })])
    await renderConnected()

    act(() => handlers.CustomModelDeploymentStateChanged({ ...customModel(), removed: true }))

    expect(cachedRows().map((m) => m.id)).toEqual(['model-2'])
  })

  it('refetches the list when an event names a model it has not cached', async () => {
    seedList([customModel()])
    await renderConnected()
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')

    act(() => handlers.CustomModelDeploymentProgress(progress({ customModelId: 'model-unknown' })))

    expect(invalidate).toHaveBeenCalledWith({ queryKey: [...CUSTOM_MODELS_QUERY_KEYS.all, 'list'] })
    expect(cachedRows()).toHaveLength(1)
  })

  it('invalidates every custom-models query on reconnect so REST fills in anything missed', async () => {
    const { result } = await renderConnected()
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')

    act(() => onreconnecting?.())
    expect(result.current.isLive).toBe(false)
    expect(result.current.connectionLost).toBe(true)

    act(() => onreconnected?.())

    expect(result.current.isLive).toBe(true)
    expect(result.current.connectionLost).toBe(false)
    expect(invalidate).toHaveBeenCalledWith({ queryKey: CUSTOM_MODELS_QUERY_KEYS.all })
  })

  it('reports the connection as lost once it closes', async () => {
    const { result } = await renderConnected()

    act(() => onclose?.())

    expect(result.current.isLive).toBe(false)
    expect(result.current.connectionLost).toBe(true)
  })

  it('does not report a lost connection while the first connect is still pending', () => {
    startResult = new Promise(() => undefined)

    const { result } = renderHook(() => useCustomModelDeploymentsHub(), { wrapper })

    expect(result.current.isLive).toBe(false)
    expect(result.current.connectionLost).toBe(false)
  })

  it('surfaces a failed start as a lost connection and logs it, leaving no unhandled rejection', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    startResult = Promise.reject(new Error('connection refused'))

    const { result } = renderHook(() => useCustomModelDeploymentsHub(), { wrapper })
    await act(async () => {
      await startResult.catch(() => undefined)
    })

    expect(result.current.isLive).toBe(false)
    expect(result.current.connectionLost).toBe(true)
    expect(warn).toHaveBeenCalled()
    warn.mockRestore()
  })
})
