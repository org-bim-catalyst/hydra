import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router'
import { ApiError } from '../api/httpClient'
import { ConsentGate } from '../features/consent/components/ConsentGate'
import { useSession } from '../features/auth/hooks/useSession'
import { RouteLoading } from './RouteLoading'

export function ProtectedRoute({ children }: PropsWithChildren) {
  const { data, isPending, error } = useSession()

  if (isPending) {
    return <RouteLoading />
  }

  // A 401 just means "not authenticated" (handled below by the redirect); anything else is
  // a real backend/network failure and must be visibly surfaced, not silently treated as
  // "you're logged out" — let the route's errorElement (ErrorPage) render it.
  if (error && !(error instanceof ApiError && error.status === 401)) {
    throw error
  }

  return data?.authenticated ? <ConsentGate>{children}</ConsentGate> : <Navigate to="/login" replace />
}
