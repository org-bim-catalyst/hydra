import type { PropsWithChildren } from 'react'
import { Navigate, useLocation } from 'react-router'
import { useSession } from '../features/auth/hooks/useSession'
import { RouteLoading } from './RouteLoading'
import { VIEW_LANDING_STATE } from './viewLandingState'

/**
 * Inverse of `ProtectedRoute` (spec.md FR-015): renders its children only for signed-out
 * visitors. An already-authenticated visitor is redirected straight into the workspace
 * instead of seeing the marketing landing page again. Applied only to `/` — auth-flow
 * routes (`/login`, `/register`, ...) are intentionally left unwrapped, matching today's
 * behavior (contracts/routing-and-consent-contract.md).
 *
 * A signed-in visitor who deliberately asks for the landing page — the Studio's Home button —
 * navigates with {@link VIEW_LANDING_STATE} and is let through. The landing page's own
 * "Start Designing" action already takes a signed-in visitor back to `/studio`.
 *
 * This guard is a cosmetic convenience, not a security boundary, so any error (401 or
 * otherwise) fails open toward showing the public page rather than blocking it.
 */
export function PublicOnlyRoute({ children }: PropsWithChildren) {
  const { data, isPending, error } = useSession()
  const location = useLocation()
  const wantsLanding = (location.state as Partial<typeof VIEW_LANDING_STATE> | null)?.viewLanding === true

  if (wantsLanding) {
    return children
  }

  if (isPending) {
    return <RouteLoading />
  }

  return !error && data?.authenticated ? <Navigate to="/studio" replace /> : children
}
