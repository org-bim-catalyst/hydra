import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../../../store/authStore'
import { NOTIFICATIONS_QUERY_KEY, NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY } from './useNotifications'
import { useNotificationHub } from './useNotificationHub'

const handlers: Record<string, (payload: unknown) => void> = {}
let startResult: Promise<void> = Promise.resolve()
let onreconnected: (() => void) | undefined

// Mirrors documents/hooks/useNotificationHub.test.tsx's mock, the established pattern for testing
// a `keepHubConnected`-based hub hook without a real SignalR connection.
vi.mock('@microsoft/signalr', () => {
  class MockHubConnectionBuilder {
    withUrl() {
      return this
    }
    withAutomaticReconnect() {
      return this
    }
    configureLogging() {
      return this
    }
    build() {
      return {
        on: (event: string, handler: (payload: unknown) => void) => {
          handlers[event] = handler
        },
        onreconnected: (cb: () => void) => {
          onreconnected = cb
        },
        onreconnecting: () => {},
        onclose: () => {},
        start: () => startResult,
        stop: () => Promise.resolve(),
      }
    }
  }
  return { HubConnectionBuilder: MockHubConnectionBuilder, LogLevel: { Warning: 2 } }
})

let queryClient: QueryClient

function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
}

const listQueryKey = [...NOTIFICATIONS_QUERY_KEY, 'list', {}]

function seedListCache() {
  queryClient.setQueryData(listQueryKey, {
    pages: [{ items: [{ id: 'existing', title: 'Existing', message: 'm', priority: 'Normal' }], nextCursor: null }],
    pageParams: [undefined],
  })
}

describe('useNotificationHub', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 'test-token', userId: 'u1' })
    startResult = Promise.resolve()
    onreconnected = undefined
    queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  })

  it('prepends the new item to the cached list and bumps the unread count on notificationCreated', async () => {
    seedListCache()
    const { result } = renderHook(() => useNotificationHub(), { wrapper })
    await act(async () => {
      await Promise.resolve()
    })

    act(() =>
      handlers['notificationCreated']?.({
        id: 'new-1',
        category: 'Workflow',
        type: 'workflow.execution.failed',
        title: 'Workflow failed',
        message: 'Stopped at step 3.',
        priority: 'Normal',
        status: 'Delivered',
        language: 'en',
        createdAtUtc: new Date().toISOString(),
        readAtUtc: null,
        expiresAtUtc: null,
        action: null,
        relatedItem: null,
        unreadCount: 5,
      }),
    )

    const cached = queryClient.getQueryData<{ pages: { items: { id: string }[] }[] }>(listQueryKey)
    expect(cached?.pages[0].items.map((item) => item.id)).toEqual(['new-1', 'existing'])
    expect(queryClient.getQueryData(NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY)).toEqual({ count: 5 })
    expect(result.current.isLive).toBe(true)
  })

  it('raises an announcement only for High/Critical priority, not Normal', async () => {
    const { result } = renderHook(() => useNotificationHub(), { wrapper })
    await act(async () => {
      await Promise.resolve()
    })

    act(() =>
      handlers['notificationCreated']?.({
        id: 'low-1',
        category: 'System',
        type: 'x',
        title: 'Normal notice',
        message: 'm',
        priority: 'Normal',
        status: 'Delivered',
        language: 'en',
        createdAtUtc: new Date().toISOString(),
        readAtUtc: null,
        expiresAtUtc: null,
        action: null,
        relatedItem: null,
        unreadCount: 1,
      }),
    )
    expect(result.current.latestAnnouncement).toBeNull()

    act(() =>
      handlers['notificationCreated']?.({
        id: 'high-1',
        category: 'Security',
        type: 'x',
        title: 'Security alert',
        message: 'm',
        priority: 'Critical',
        status: 'Delivered',
        language: 'en',
        createdAtUtc: new Date().toISOString(),
        readAtUtc: null,
        expiresAtUtc: null,
        action: null,
        relatedItem: null,
        unreadCount: 2,
      }),
    )
    expect(result.current.latestAnnouncement).toEqual({ id: 'high-1', title: 'Security alert', message: 'm', priority: 'Critical' })

    act(() => result.current.dismissAnnouncement())
    expect(result.current.latestAnnouncement).toBeNull()
  })

  it('invalidates the list and unread-count queries on reconnect', async () => {
    renderHook(() => useNotificationHub(), { wrapper })
    await act(async () => {
      await Promise.resolve()
    })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    act(() => onreconnected?.())

    const invalidatedKeys = invalidateSpy.mock.calls.map((call) => (call[0] as { queryKey: unknown[] }).queryKey)
    expect(invalidatedKeys).toContainEqual([...NOTIFICATIONS_QUERY_KEY, 'list'])
    expect(invalidatedKeys).toContainEqual(NOTIFICATIONS_UNREAD_COUNT_QUERY_KEY)
  })

  // T059 deviation: this codebase's SignalR auth is a cookie the browser sends automatically
  // (`AccessTokenCookie.cs`) — no hub hook anywhere uses an `accessTokenFactory` (confirmed via a
  // zero-result repo-wide search), so there is no per-call token re-read to test. What the literal
  // "frozen-token regression" bullet actually guards against here is this hook using
  // `keepHubConnected` (rather than a hand-rolled `connection.start().catch(() => undefined)`,
  // the exact anti-pattern the 1205b21 fix and constitution §2.VIII forbid) — that is what this
  // hook does, and it's exercised by the isLive/reconnect assertions above.
  it('starts the connection through keepHubConnected, not a hand-rolled start().catch(() => undefined)', async () => {
    startResult = Promise.reject(new Error('connection refused'))
    const { result } = renderHook(() => useNotificationHub(), { wrapper })

    await act(async () => {
      await startResult.catch(() => undefined)
    })

    // A silently-discarded start would leave isLive stuck at its initial `false` with no signal
    // that anything went wrong; keepHubConnected instead retries and reports the failure via isLive.
    expect(result.current.isLive).toBe(false)
  })
})
