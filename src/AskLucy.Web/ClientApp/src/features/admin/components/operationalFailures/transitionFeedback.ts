import { ApiError } from '../../../../api/httpClient'
import type { BulkTransitionResult } from '../../api/adminOperationalFailuresApi'

export interface TransitionFeedback {
  severity: 'success' | 'warning' | 'error'
  text: string
  /** Set when a reopen collided with a newer incident for the same cause; the toast links to it. */
  newerIncidentId?: string
}

export const CHANGED_BY_SOMEONE_ELSE = 'This incident was changed by someone else — reloaded.'

export const isIncidentConflict = (error: unknown) => error instanceof ApiError && error.status === 409

/** specs/074 FR-024/FR-027 — what a failed transition tells the administrator. A 409 also reloads. */
export function describeTransitionError(error: unknown): TransitionFeedback {
  if (error instanceof ApiError && error.status === 409) {
    return error.newerIncidentId
      ? { severity: 'warning', text: 'A newer incident is already open for this cause.', newerIncidentId: error.newerIncidentId }
      : { severity: 'warning', text: CHANGED_BY_SOMEONE_ELSE }
  }
  return {
    severity: 'error',
    text: error instanceof ApiError ? (error.detail ?? error.message) : 'Could not update the incident.',
  }
}

/** "Resolved 38, skipped 1, failed 1" — skipped and failed are said only when there are any. */
export function describeBulkResult(verb: string, result: BulkTransitionResult) {
  const parts = [`${verb} ${result.succeeded}`]
  if (result.skipped > 0) parts.push(`skipped ${result.skipped}`)
  if (result.failed.length > 0) parts.push(`failed ${result.failed.length}`)
  return parts.join(', ')
}
