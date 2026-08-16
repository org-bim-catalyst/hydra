import type { PropsWithChildren } from 'react'
import { usePublicCookieConsent } from '../hooks/usePublicCookieConsent'
import { PublicConsentBanner } from './PublicConsentBanner'

/**
 * Anonymous-safe companion to `ConsentGate` (research.md Topic 2, contracts/routing-and-
 * consent-contract.md) for the public landing/auth-flow pages. Unlike `ConsentGate`, this
 * never blocks page content behind a full-page loading/error state — the underlying
 * `/cookie-policy` lookup is a background check; content is always rendered immediately,
 * and only the banner itself is conditional on the resolved consent state.
 */
export function PublicConsentGate({ children }: PropsWithChildren) {
  const { data } = usePublicCookieConsent()

  return (
    <>
      {children}
      {data?.requiresReconsent && <PublicConsentBanner />}
    </>
  )
}
