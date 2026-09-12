import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../../../store/authStore'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import { useFloatingPanelHub } from './useFloatingPanelHub'

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

describe('useFloatingPanelHub', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 'test-token', userId: 'u1' })
    startResult = Promise.resolve()
    onreconnected = undefined
    onreconnecting = undefined
    onclose = undefined
  })

  it('dispatches a received PanelRequested payload into floatingPanelStore.openPanel', async () => {
    const openPanelSpy = vi.spyOn(useFloatingPanelStore.getState(), 'openPanel')

    renderHook(() => useFloatingPanelHub())
    await act(async () => {
      await Promise.resolve()
    })

    const payload = { kind: 'live' as const, requestId: 'r1', typeKey: 'some-live-panel-kind', title: 'T', data: {} }
    handlers['PanelRequested']?.(payload)

    expect(openPanelSpy).toHaveBeenCalledWith(payload)
  })

  it('does not connect when there is no access token', () => {
    useAuthStore.setState({ accessToken: null, userId: null })
    delete handlers['PanelRequested']

    renderHook(() => useFloatingPanelHub())

    expect(handlers['PanelRequested']).toBeUndefined()
  })

  // specs/029-fix-chat-widget-bugs T004d/FR-010/analysis finding C1 — this hook previously
  // called `connection.start().catch(() => undefined)`, silently discarding a failed
  // connection with no trace. These two cases assert the fix: the failure is now exposed via
  // `isLive`, matching the already-compliant sibling hooks' pattern.
  it('exposes isLive: true once the connection starts successfully', async () => {
    const { result } = renderHook(() => useFloatingPanelHub())

    await act(async () => {
      await Promise.resolve()
    })

    expect(result.current.isLive).toBe(true)
  })

  it('exposes isLive: false, not a silently discarded failure, when the connection fails to start', async () => {
    startResult = Promise.reject(new Error('connection refused'))

    const { result } = renderHook(() => useFloatingPanelHub())

    await act(async () => {
      await startResult.catch(() => undefined)
    })

    expect(result.current.isLive).toBe(false)
  })

  it('tracks reconnecting/reconnected/closed transitions via isLive', async () => {
    const { result } = renderHook(() => useFloatingPanelHub())
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
})
