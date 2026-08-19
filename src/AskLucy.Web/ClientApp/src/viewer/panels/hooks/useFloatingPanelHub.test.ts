import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../../../store/authStore'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import { useFloatingPanelHub } from './useFloatingPanelHub'

const handlers: Record<string, (payload: unknown) => void> = {}

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
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
      }
    }
  }
  return { HubConnectionBuilder: MockHubConnectionBuilder, LogLevel: { Warning: 2 } }
})

describe('useFloatingPanelHub', () => {
  beforeEach(() => {
    useAuthStore.setState({ accessToken: 'test-token', refreshToken: null, userId: 'u1' })
  })

  it('dispatches a received PanelRequested payload into floatingPanelStore.openPanel', async () => {
    const openPanelSpy = vi.spyOn(useFloatingPanelStore.getState(), 'openPanel')

    renderHook(() => useFloatingPanelHub())
    await act(async () => {
      await Promise.resolve()
    })

    const payload = { requestId: 'r1', typeKey: 'table', title: 'T', data: {} }
    handlers['PanelRequested']?.(payload)

    expect(openPanelSpy).toHaveBeenCalledWith(payload)
  })

  it('does not connect when there is no access token', () => {
    useAuthStore.setState({ accessToken: null, refreshToken: null, userId: null })
    delete handlers['PanelRequested']

    renderHook(() => useFloatingPanelHub())

    expect(handlers['PanelRequested']).toBeUndefined()
  })
})
