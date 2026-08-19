import { useAuthStore } from '../store/authStore'

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

export class ApiError extends Error {
  status: number
  detail?: string
  /** The `errors` Problem Details extension (`ProblemDetailsMiddleware.cs`) — per-field/per-entry messages for a `validation-failed` (400) response. Undefined for every other error shape. */
  errors?: Record<string, string[]>

  constructor(status: number, message: string, detail?: string, errors?: Record<string, string[]>) {
    super(message)
    this.status = status
    this.detail = detail
    this.errors = errors
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
    throw new ApiError(response.status, problem?.title ?? 'Request failed', problem?.detail, problem?.errors)
  }

  if (response.status === 204) {
    return undefined as T
  }

  // Read as text first: some 2xx responses (e.g. 202 Accepted from fire-and-forget
  // endpoints like the funnel-analytics recorder) have no body at all, and `.json()`
  // throws a SyntaxError on an empty string rather than returning something falsy.
  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}
