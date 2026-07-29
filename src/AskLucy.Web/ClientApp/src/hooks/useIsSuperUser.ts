import { jwtDecode } from 'jwt-decode'
import { useAuthStore } from '../store/authStore'

interface DecodedAccessToken {
  role?: string | string[]
}

/**
 * UX affordance only — the server enforces the same check independently (FR-014's
 * privilege-escalation guard lives in ChangeUserRoleCommandHandler). Drives whether the
 * role-change action is offered in the admin UI at all.
 */
export function useIsSuperUser(): boolean {
  const accessToken = useAuthStore((s) => s.accessToken)
  if (!accessToken) return false

  try {
    const decoded = jwtDecode<DecodedAccessToken>(accessToken)
    const roles = Array.isArray(decoded.role) ? decoded.role : decoded.role ? [decoded.role] : []
    return roles.includes('Super User')
  } catch {
    return false
  }
}
