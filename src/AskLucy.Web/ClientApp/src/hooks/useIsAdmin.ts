import { jwtDecode } from 'jwt-decode'
import { useAuthStore } from '../store/authStore'

interface DecodedAccessToken {
  role?: string | string[]
}

const ADMIN_ROLES = ['Administrator', 'Super User']

/** UX affordance only — see AdminRoute for the equivalent, server-enforced check. */
export function useIsAdmin(): boolean {
  const accessToken = useAuthStore((s) => s.accessToken)
  if (!accessToken) return false

  try {
    const decoded = jwtDecode<DecodedAccessToken>(accessToken)
    const roles = Array.isArray(decoded.role) ? decoded.role : decoded.role ? [decoded.role] : []
    return roles.some((role) => ADMIN_ROLES.includes(role))
  } catch {
    return false
  }
}
