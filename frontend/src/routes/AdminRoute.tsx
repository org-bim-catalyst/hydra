import { jwtDecode } from 'jwt-decode'
import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router'
import { useAuthStore } from '../store/authStore'

interface DecodedAccessToken {
  role?: string | string[]
}

const ADMIN_ROLES = ['Administrator', 'Super User']

/**
 * UX affordance only, not the security boundary (FR-017, User Story 4) — the server
 * enforces the same role check independently via the `AdministratorOrSuperUser`
 * authorization policy on every admin endpoint. This just avoids showing a
 * non-admin a page that would immediately 403.
 */
export function AdminRoute({ children }: PropsWithChildren) {
  const accessToken = useAuthStore((s) => s.accessToken)

  if (!accessToken) {
    return <Navigate to="/login" replace />
  }

  let decoded: DecodedAccessToken
  try {
    decoded = jwtDecode<DecodedAccessToken>(accessToken)
  } catch {
    return <Navigate to="/login" replace />
  }

  const roles = Array.isArray(decoded.role) ? decoded.role : decoded.role ? [decoded.role] : []
  const isAdmin = roles.some((role) => ADMIN_ROLES.includes(role))
  return isAdmin ? <>{children}</> : <Navigate to="/chat" replace />
}
