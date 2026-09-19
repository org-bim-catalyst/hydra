import { useState } from 'react'
import { ApiError } from '../../../api/httpClient'
import { postHangfireSession } from '../api/adminHangfireApi'

const POPUP_BLOCKED_MESSAGE = 'Your browser blocked the Jobs dashboard from opening. Allow pop-ups for this site and try again.'
const MINT_FAILED_MESSAGE = 'Could not open the Jobs dashboard. Please try again.'

/**
 * Opens `/hangfire` in a new tab, authenticated via the short-lived dashboard cookie
 * (specs/060-hangfire-dashboard-access). No `?theme=` param is appended — Hangfire has no
 * query-param theme hook (research.md Decision 3); the dashboard follows the browser/OS color
 * scheme on its own.
 *
 * The blank tab is opened *before* awaiting the mint call, synchronously in the click handler —
 * browsers only allow `window.open` without popup-blocking as a direct result of a user gesture,
 * and an `await` in between would break that direct-result chain.
 */
export function useOpenHangfireDashboard() {
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [isPending, setIsPending] = useState(false)

  const open = async () => {
    setErrorMessage(null)
    const newTab = window.open('', '_blank')

    if (!newTab) {
      setErrorMessage(POPUP_BLOCKED_MESSAGE)
      return
    }

    setIsPending(true)
    try {
      await postHangfireSession()
      newTab.location.href = '/hangfire'
    } catch (err: unknown) {
      newTab.close()
      setErrorMessage(err instanceof ApiError ? (err.detail ?? err.message) : MINT_FAILED_MESSAGE)
    } finally {
      setIsPending(false)
    }
  }

  return { open, errorMessage, clearError: () => setErrorMessage(null), isPending }
}
