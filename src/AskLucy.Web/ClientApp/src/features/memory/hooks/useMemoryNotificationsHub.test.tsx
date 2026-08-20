import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../../../store/authStore'
import { useMemoryNotificationsHub } from './useMemoryNotificationsHub'

const handlers: Record<string, (payload: unknown) => void> = {}
let startResult: Promise<void> = Promise.resolve()
let onreconnected: (() => void) | undefined
let onreconnecting: (() => void) | undefined
let onclose: (() => void) | undefined

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

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
}

describe('useMemoryNotificationsHub', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 'test-token', refreshToken: null, userId: 'u1' })
    startResult = Promise.resolve()
    onreconnected = undefined
    onreconnecting = undefined
    onclose = undefined
  })

  // specs/029-fix-chat-widget-bugs T004d/FR-010/analysis finding C1 — this hook previously
  // called `connection.start().catch(() => undefined)`, silently discarding a failed
  // connection with no trace. These assert the fix: the failure is now exposed via `isLive`,
  // matching the already-compliant sibling hooks' pattern.
  it('exposes isLive: true once the connection starts successfully', async () => {
    const { result } = renderHook(() => useMemoryNotificationsHub(), { wrapper })

    await act(async () => {
      await Promise.resolve()
    })

    expect(result.current.isLive).toBe(true)
  })

  it('exposes isLive: false, not a silently discarded failure, when the connection fails to start', async () => {
    startResult = Promise.reject(new Error('connection refused'))

    const { result } = renderHook(() => useMemoryNotificationsHub(), { wrapper })

    await act(async () => {
      await startResult.catch(() => undefined)
    })

    expect(result.current.isLive).toBe(false)
  })

  it('tracks reconnecting/reconnected/closed transitions via isLive', async () => {
    const { result } = renderHook(() => useMemoryNotificationsHub(), { wrapper })
    await act(async () => {
      await Promise.resolve()
    })
    expect(result.current.isLive).toBe(true)

    act(() => onreconnecting?.())
    expect(result.current.isLive).toBe(false)

    act(() => onreconnected?.())
    expect(result.current.isLive).toBe(true)

    act(() => onclose?.())
    expect(result.current.isLive).toBe(false)
  })

  it('still surfaces latest/dismiss for a received memoryNotificationCreated event', async () => {
    const { result } = renderHook(() => useMemoryNotificationsHub(), { wrapper })
    await act(async () => {
      await Promise.resolve()
    })

    const payload = { id: 'n1', message: 'A memory was created', createdAtUtc: new Date().toISOString() }
    act(() => handlers['memoryNotificationCreated']?.(payload))

    expect(result.current.latest).toEqual(payload)

    act(() => result.current.dismiss())
    expect(result.current.latest).toBeNull()
  })
})
