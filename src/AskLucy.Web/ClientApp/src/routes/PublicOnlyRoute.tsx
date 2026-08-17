import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router'
import { useAuthStore } from '../store/authStore'

/**
 * Inverse of `ProtectedRoute` (spec.md FR-015): renders its children only for signed-out
 * visitors. An already-authenticated visitor is redirected straight into the workspace
 * instead of seeing the marketing landing page again. Applied only to `/` — auth-flow
 * routes (`/login`, `/register`, ...) are intentionally left unwrapped, matching today's
 * behavior (contracts/routing-and-consent-contract.md).
 */
export function PublicOnlyRoute({ children }: PropsWithChildren) {
  const accessToken = useAuthStore((s) => s.accessToken)
  return accessToken ? <Navigate to="/studio" replace /> : children
}
