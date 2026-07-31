import type { PropsWithChildren } from 'react'
import { Navigate } from 'react-router'
import { ConsentGate } from '../features/consent/components/ConsentGate'
import { useAuthStore } from '../store/authStore'

export function ProtectedRoute({ children }: PropsWithChildren) {
  const accessToken = useAuthStore((s) => s.accessToken)
  return accessToken ? <ConsentGate>{children}</ConsentGate> : <Navigate to="/login" replace />
}
