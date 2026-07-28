import { useAuthStore } from '../store/authStore'

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

export class ApiError extends Error {
  status: number
  detail?: string

  constructor(status: number, message: string, detail?: string) {
    super(message)
    this.status = status
    this.detail = detail
  }
}

/**
 * JWT-aware fetch wrapper. Attaches the access token, and on a 401 redirects to login
 * (FR-015/User Story 2) rather than retrying silently, since refresh-token rotation is
 * handled explicitly by the auth feature, not implicitly here.
 */
export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const accessToken = useAuthStore.getState().accessToken

  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...init.headers,
    },
  })

  if (response.status === 401) {
    useAuthStore.getState().clear()
    window.location.assign('/login')
    throw new ApiError(401, 'Authentication required')
  }

  if (!response.ok) {
    const problem = await response.json().catch(() => undefined)
    throw new ApiError(response.status, problem?.title ?? 'Request failed', problem?.detail)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}
