import { useSession } from './useSession'

/**
 * The signed-in user's effective admin-panel permission keys, straight from the session check
 * (server-resolved fresh every time — see CurrentAuthorizationClaimsTransformation). This is a
 * UX affordance only, same as `useIsAdmin` — the server enforces every permission independently.
 */
export function usePermissions(): string[] {
  const { data } = useSession()
  return data?.permissions ?? []
}

export function useCan(key: string): boolean {
  return usePermissions().includes(key)
}

export function useCanAny(keys: string[]): boolean {
  const permissions = usePermissions()
  return keys.some((key) => permissions.includes(key))
}
