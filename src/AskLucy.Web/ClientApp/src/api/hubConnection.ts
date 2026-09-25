import type { HubConnection } from '@microsoft/signalr'
import { attemptSilentRefresh } from './httpClient'

/** Waits between restart attempts once a connection is down; the last value repeats. */
const RESTART_DELAYS_MS = [2_000, 5_000, 10_000, 30_000]

/**
 * A hub handshake refused for authentication. Duck-typed rather than `instanceof HttpError` so a
 * test that mocks `@microsoft/signalr` without that class still exercises this path.
 */
function isUnauthorized(error: unknown): boolean {
  return (error as { statusCode?: unknown } | null)?.statusCode === 401
}

/**
 * Starts `connection` and keeps it started until the returned disposer runs, reporting every
 * change through `setIsLive` — the only signal a user gets that live updates have stopped.
 *
 * <p><b>Why this exists.</b> Every hub hook used to call `start()` once. A start that failed was
 * never tried again, and `withAutomaticReconnect()` gives up after four attempts (~42 s) and
 * closes for good, so either way the connection stayed dead — "Reconnecting…" pinned on the
 * viewer while site-analysis results were written to the chat but never pushed to it. The usual
 * cause is authentication: hubs authenticate with the short-lived access-token cookie
 * (`AccessTokenCookie.cs`), the auth store is restored from storage on reload, so a hook dials
 * immediately with a token-shaped session whose cookie expired minutes ago, and nothing re-issues
 * the cookie until some REST call happens to 401. A refused handshake now runs the same silent
 * refresh a REST 401 does, which re-issues the cookie, then retries.</p>
 *
 * <p>`onReconnected` runs whenever the connection comes back after being lost — through SignalR's
 * own reconnect or a restart from here — so callers can refetch whatever they missed. It does not
 * run on the first successful start.</p>
 */
export function keepHubConnected(
  connection: HubConnection,
  setIsLive: (live: boolean) => void,
  onReconnected?: () => void,
): () => void {
  let disposed = false
  let hasConnected = false
  let failures = 0
  let retryTimer: ReturnType<typeof setTimeout> | undefined

  const start = async () => {
    try {
      await connection.start()
      if (disposed) return
      failures = 0
      setIsLive(true)
      if (hasConnected) onReconnected?.()
      hasConnected = true
    } catch (error) {
      if (disposed) return
      setIsLive(false)
      console.warn('A live-update connection could not start; retrying.', error)
      // Never rejects: a refresh that fails leaves the next attempt to fail the same way, and
      // the loop keeps trying at the slowest cadence rather than giving up on a session that a
      // later sign-in or network recovery would revive.
      if (isUnauthorized(error)) await attemptSilentRefresh()
      if (disposed) return
      retryTimer = setTimeout(() => void start(), RESTART_DELAYS_MS[Math.min(failures++, RESTART_DELAYS_MS.length - 1)])
    }
  }

  connection.onreconnecting(() => setIsLive(false))
  connection.onreconnected(() => {
    setIsLive(true)
    onReconnected?.()
  })
  // SignalR's own reconnect has already waited out its schedule by the time this fires (or the
  // disposer below stopped the connection, which is ignored), so restart straight away.
  connection.onclose(() => {
    if (disposed) return
    setIsLive(false)
    void start()
  })

  void start()

  return () => {
    disposed = true
    clearTimeout(retryTimer)
    connection.stop().catch((error: unknown) => console.warn('A live-update connection did not stop cleanly.', error))
  }
}
