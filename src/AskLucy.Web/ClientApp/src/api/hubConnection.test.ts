import { HttpClient, HttpError, HubConnectionBuilder, LogLevel, type HubConnection, type HttpResponse } from '@microsoft/signalr'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { keepHubConnected } from './hubConnection'

const attemptSilentRefresh = vi.fn<() => Promise<boolean>>()
vi.mock('./httpClient', () => ({ attemptSilentRefresh: () => attemptSilentRefresh() }))

function fakeConnection(startResults: (() => Promise<void>)[]) {
  const callbacks: { reconnecting?: () => void; reconnected?: () => void; close?: () => void } = {}
  const start = vi.fn(() => (startResults.shift() ?? (() => Promise.resolve()))())
  const stop = vi.fn(() => Promise.resolve())
  const connection = {
    start,
    stop,
    onreconnecting: (cb: () => void) => (callbacks.reconnecting = cb),
    onreconnected: (cb: () => void) => (callbacks.reconnected = cb),
    onclose: (cb: () => void) => (callbacks.close = cb),
  } as unknown as HubConnection
  return { connection, start, stop, callbacks }
}

const refused = () => Promise.reject(Object.assign(new Error('Unauthorized'), { statusCode: 401 }))
const unreachable = () => Promise.reject(new Error('Failed to fetch'))

describe('keepHubConnected', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    attemptSilentRefresh.mockReset().mockResolvedValue(true)
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  // Live-testing report, 2026-09-25: "Reconnecting…" stayed on the viewer and both site-analysis
  // results were written to the chat but never pushed to it. A reloaded page dials the hub with
  // a session restored from storage whose hub cookie expired long ago; the refused handshake was
  // never retried, so the connection stayed dead for the whole session.
  it('re-issues the hub cookie and retries when the handshake is refused', async () => {
    const { connection, start } = fakeConnection([refused])
    const setIsLive = vi.fn()

    keepHubConnected(connection, setIsLive)
    await vi.advanceTimersByTimeAsync(0)
    expect(setIsLive).toHaveBeenLastCalledWith(false)
    expect(attemptSilentRefresh).toHaveBeenCalledTimes(1)

    await vi.advanceTimersByTimeAsync(2_000)
    expect(start).toHaveBeenCalledTimes(2)
    expect(setIsLive).toHaveBeenLastCalledWith(true)
  })

  // Production log, 2026-09-25: a 401 negotiate retried every 30 s with no /auth/refresh between
  // attempts. The fake above rejects with `statusCode` on the error, which the real client never
  // does — it wraps the refused negotiate in an error that keeps only the message. Drive a real
  // HubConnection whose negotiate is refused so this can't drift from the library again.
  it('re-issues the hub cookie when a real negotiate is refused', async () => {
    class RefusingHttpClient extends HttpClient {
      send(): Promise<HttpResponse> {
        return Promise.reject(new HttpError('Unauthorized', 401))
      }
    }
    const connection = new HubConnectionBuilder()
      .withUrl('https://example.test/hubs/panels', { httpClient: new RefusingHttpClient() })
      .configureLogging(LogLevel.None)
      .build()
    const setIsLive = vi.fn()

    const dispose = keepHubConnected(connection, setIsLive)
    await vi.advanceTimersByTimeAsync(0)

    expect(setIsLive).toHaveBeenLastCalledWith(false)
    expect(attemptSilentRefresh).toHaveBeenCalledTimes(1)
    dispose()
  })

  it('keeps retrying an unreachable hub without touching the session', async () => {
    const { connection, start } = fakeConnection([unreachable, unreachable, unreachable])
    const setIsLive = vi.fn()

    keepHubConnected(connection, setIsLive)
    await vi.advanceTimersByTimeAsync(2_000 + 5_000 + 10_000)

    expect(start).toHaveBeenCalledTimes(4)
    expect(setIsLive).toHaveBeenLastCalledWith(true)
    expect(attemptSilentRefresh).not.toHaveBeenCalled()
  })

  // withAutomaticReconnect gives up after four attempts and closes; nothing used to start it again.
  it('restarts after SignalR gives up reconnecting, and reports it as a reconnect', async () => {
    const { connection, start, callbacks } = fakeConnection([])
    const setIsLive = vi.fn()
    const onReconnected = vi.fn()

    keepHubConnected(connection, setIsLive, onReconnected)
    await vi.advanceTimersByTimeAsync(0)
    expect(onReconnected).not.toHaveBeenCalled()

    callbacks.close?.()
    expect(setIsLive).toHaveBeenLastCalledWith(false)
    await vi.advanceTimersByTimeAsync(0)

    expect(start).toHaveBeenCalledTimes(2)
    expect(setIsLive).toHaveBeenLastCalledWith(true)
    expect(onReconnected).toHaveBeenCalledTimes(1)
  })

  it('stops for good once disposed, including a retry already scheduled', async () => {
    const { connection, start, stop, callbacks } = fakeConnection([unreachable])
    const setIsLive = vi.fn()

    const dispose = keepHubConnected(connection, setIsLive)
    await vi.advanceTimersByTimeAsync(0)
    dispose()
    callbacks.close?.()
    await vi.advanceTimersByTimeAsync(60_000)

    expect(stop).toHaveBeenCalledTimes(1)
    expect(start).toHaveBeenCalledTimes(1)
  })
})
