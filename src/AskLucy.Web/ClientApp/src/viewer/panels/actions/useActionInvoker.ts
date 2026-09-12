import { useCallback, useState } from 'react'
import { viewerEngine } from '../../engine/viewerEngineInstance'
import { actionAllowlist, validateAction } from './allowlist'
import type { ActionRequest } from '../content/blocks'

/** Per-affordance invocation state (spec FR-015): an action performed but unable to take effect —
 * its target no longer exists, or the viewer refuses it — must tell the user, not appear to
 * succeed or silently do nothing. Kept local to the entry that was activated rather than a
 * page-wide toast, so the failure reads as "this row's action didn't work" rather than an
 * unrelated global notification. */
export function useActionInvoker() {
  const [error, setError] = useState<string | null>(null)

  const invoke = useCallback((action: ActionRequest) => {
    // Defensive, not the primary gate: `ActionAffordance` already refuses to render an entry
    // whose action fails validation, so this only re-confirms it can never be reached by a
    // stale reference (research D6 — validated at render, invoked here).
    const validation = validateAction(action.command, action.args)
    if (!validation.valid) {
      setError(validation.reason)
      return
    }

    const result = actionAllowlist[action.command].invoke(viewerEngine, action.args)
    setError(result.ok ? null : (result.error ?? 'This action could not be carried out.'))
  }, [])

  return { invoke, error }
}
