import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router'
import { useAuthStore } from '../store/authStore'

export function ProtectedRoute({ children }: PropsWithChildren) {
  const accessToken = useAuthStore((s) => s.accessToken)
  return accessToken ? <>{children}</> : <Navigate to="/login" replace />
}
