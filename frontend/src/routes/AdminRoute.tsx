import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router'
import { useIsAdmin } from '../hooks/useIsAdmin'
import { useAuthStore } from '../store/authStore'

/**
 * UX affordance only, not the security boundary (FR-017, User Story 4) — the server
 * enforces the same role check independently via the `AdministratorOrSuperUser`
 * authorization policy on every admin endpoint. This just avoids showing a
 * non-admin a page that would immediately 403.
 */
export function AdminRoute({ children }: PropsWithChildren) {
  const accessToken = useAuthStore((s) => s.accessToken)
  const isAdmin = useIsAdmin()

  if (!accessToken) {
    return <Navigate to="/login" replace />
  }

  return isAdmin ? <>{children}</> : <Navigate to="/chat" replace />
}
