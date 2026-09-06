import { useQuery } from '@tanstack/react-query'
import * as authApi from '../api/authApi'

export const SESSION_QUERY_KEY = ['auth', 'session']

/**
 * Backs the route guards (ProtectedRoute/AdminRoute/PublicOnlyRoute): validated against the
 * httpOnly refresh-token cookie server-side, decoupled from the 15-minute access token so
 * navigation doesn't bounce a user to /login while their 14-day session is still valid.
 * `staleTime` means only the first check per minute is async — later client-side navigations
 * read the cache synchronously.
 */
export function useSession() {
  return useQuery({ queryKey: SESSION_QUERY_KEY, queryFn: authApi.getSession, staleTime: 60_000 })
}
