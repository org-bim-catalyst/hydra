import { ApiError } from '../api/httpClient'

/**
 * How many times a failed query is retried before its error is shown.
 *
 * A 4xx is the caller's own fault and is never retried. Any other failure gets two retries, except
 * a 503: that is the server saying "come back shortly" — which is what a deploy's host restart
 * answers for its ~16 s (found live 2026-09-25: the studio's weather card failed inside that
 * window, spent its two retries within 3 s, and stayed "Weather unavailable" for the session).
 * Five retries on the default back-off (1, 2, 4, 8, 16 s) cover about 31 s, so a query started
 * during a restart lands once the host is back.
 */
export function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  if (error instanceof ApiError && error.status < 500) return false
  if (error instanceof ApiError && error.status === 503) return failureCount < 5
  return failureCount < 2
}
