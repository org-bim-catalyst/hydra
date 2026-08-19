import { useQueryClient } from '@tanstack/react-query'
import { useCallback } from 'react'
import { ensurePublicConsentState } from '../../consent/hooks/usePublicCookieConsent'
import { recordFunnelEvent } from '../api/analyticsApi'
import type { FunnelCtaId, FunnelKind } from '../api/analyticsApi'

const SESSION_ID_STORAGE_KEY = 'flumeria_funnel_session_id'

function getOrCreateSessionId(): string {
  const existing = sessionStorage.getItem(SESSION_ID_STORAGE_KEY)
  if (existing) return existing

  const created = crypto.randomUUID()
  sessionStorage.setItem(SESSION_ID_STORAGE_KEY, created)
  return created
}

/**
 * Consent-gated, fire-and-forget funnel/CTA analytics (spec.md FR-021, contracts/routing-
 * and-consent-contract.md). Checks the public (cookie-based) consent state — the only
 * source relevant on the public landing/auth pages this hook is used from; a visitor who
 * has just authenticated by the moment `recordFunnelCompleted('SignIn')` fires was still
 * signed out for the rest of that same page's lifetime, so the public consent cookie
 * governs consistently rather than racing an authenticated-consent lookup.
 *
 * Uses `ensurePublicConsentState` (an imperative, call-time query read) rather than this
 * hook's own rendered `usePublicCookieConsent()` snapshot: some callers fire the event from
 * inside another async operation's completion handler (e.g. the OAuth exchange in
 * `ExternalLoginCompletePage`), which can resolve before the consent query itself has
 * settled on a fast/mocked network — reading a closed-over render value at that instant
 * would see `undefined` and silently drop a real, consented event. `ensureQueryData`
 * always waits for (or reuses) the settled result instead.
 *
 * A failed call is caught and logged to the console at most — it must never throw, block,
 * or delay the caller's own navigation (plan.md Constitution Check, Principle VIII note).
 */
export function useFunnelAnalytics() {
  const queryClient = useQueryClient()

  const fireEvent = useCallback(
    (
      input:
        | { eventType: 'CtaClicked'; ctaId: FunnelCtaId }
        | { eventType: 'FunnelCompleted'; funnelType: FunnelKind },
    ) => {
      ensurePublicConsentState(queryClient)
        .then((consent) => {
          if (!consent.analytics) return
          return recordFunnelEvent({
            ...input,
            sessionId: getOrCreateSessionId(),
            occurredAtUtc: new Date().toISOString(),
          })
        })
        .catch((error: unknown) => {
          // Best-effort telemetry; never surfaced to the user (FR-021).
          console.warn('Funnel analytics event failed to record', error)
        })
    },
    [queryClient],
  )

  const recordCtaClick = useCallback((ctaId: FunnelCtaId) => fireEvent({ eventType: 'CtaClicked', ctaId }), [fireEvent])
  const recordFunnelCompleted = useCallback(
    (funnelType: FunnelKind) => fireEvent({ eventType: 'FunnelCompleted', funnelType }),
    [fireEvent],
  )

  return { recordCtaClick, recordFunnelCompleted }
}
