import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router'
import { ApiError } from '../api/httpClient'
import { useSession } from '../features/auth/hooks/useSession'
import { ADMIN_ROLES } from '../hooks/useIsAdmin'
import { RouteLoading } from './RouteLoading'

export interface AdminRouteProps extends PropsWithChildren {
  /**
   * One or more permission keys (specs/055-role-management) — the route is reachable if the
   * caller holds ANY of them. Omit for a screen reserved to built-in roles regardless of
   * permissions (Roles/Role assignments/Permissions themselves, FR-002) — those fall back to
   * the plain admin-role check below.
   */
  permission?: string | string[]
}

/**
 * UX affordance only, not the security boundary (FR-017, User Story 4; FR-002/FR-005 for the
 * permission-gated case) — the server enforces the equivalent check independently, via either
 * the `AdministratorOrSuperUser` authorization policy or a `[RequirePermission]` attribute on
 * every admin endpoint. This just avoids showing a route that would immediately 403.
 */
export function AdminRoute({ children, permission }: AdminRouteProps) {
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

  const isBuiltInAdmin = data.roles.some((role) => ADMIN_ROLES.includes(role))
  const keys = permission === undefined ? [] : Array.isArray(permission) ? permission : [permission]
  const isAllowed = isBuiltInAdmin || (keys.length > 0 && keys.some((key) => data.permissions.includes(key)))

  return isAllowed ? <>{children}</> : <Navigate to="/studio" replace />
}
