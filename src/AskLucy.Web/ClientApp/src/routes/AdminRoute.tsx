import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router'
import { ApiError } from '../api/httpClient'
import { useSession } from '../features/auth/hooks/useSession'
import { ADMIN_ROLES } from '../hooks/useIsAdmin'
import { RouteLoading } from './RouteLoading'

/**
 * UX affordance only, not the security boundary (FR-017, User Story 4) — the server
 * enforces the same role check independently via the `AdministratorOrSuperUser`
 * authorization policy on every admin endpoint. This just avoids showing a
 * non-admin a page that would immediately 403.
 */
export function AdminRoute({ children }: PropsWithChildren) {
  const { data, isPending, error } = useSession()

  if (isPending) {
    return <RouteLoading />
  }

  if (error && !(error instanceof ApiError && error.status === 401)) {
    throw error
  }

  if (!data?.authenticated) {
    return <Navigate to="/login" replace />
  }

  const isAdmin = data.roles.some((role) => ADMIN_ROLES.includes(role))
  return isAdmin ? <>{children}</> : <Navigate to="/studio" replace />
}
