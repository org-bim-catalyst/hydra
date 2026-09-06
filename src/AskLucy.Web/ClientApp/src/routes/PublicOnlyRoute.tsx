import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router'
import { useSession } from '../features/auth/hooks/useSession'
import { RouteLoading } from './RouteLoading'

/**
 * Inverse of `ProtectedRoute` (spec.md FR-015): renders its children only for signed-out
 * visitors. An already-authenticated visitor is redirected straight into the workspace
 * instead of seeing the marketing landing page again. Applied only to `/` — auth-flow
 * routes (`/login`, `/register`, ...) are intentionally left unwrapped, matching today's
 * behavior (contracts/routing-and-consent-contract.md).
 *
 * This guard is a cosmetic convenience, not a security boundary, so any error (401 or
 * otherwise) fails open toward showing the public page rather than blocking it.
 */
export function PublicOnlyRoute({ children }: PropsWithChildren) {
  const { data, isPending, error } = useSession()

  if (isPending) {
    return <RouteLoading />
  }

  return !error && data?.authenticated ? <Navigate to="/studio" replace /> : children
}
