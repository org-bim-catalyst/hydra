import { useAuthStore } from '../store/authStore'

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

/** specs/043 — mirrors `AiProviderFailureKind`. */
export type ProviderFailureKind =
  | 'CredentialRejected'
  | 'CredentialUnreadable'
  | 'NotConfigured'
  | 'QuotaExhausted'
  | 'RateLimited'
  | 'UsageRestricted'
  | 'Unavailable'
  | 'RequestInvalid'
  | 'ResponseNotUnderstood'

/**
 * The `providerFailure` Problem Details extension (specs/043, contracts/provider-failure-classification.md §3).
 * Present only for administrators — the server withholds it from everyone else, so a
 * non-administrator cannot read the tenant's quota or billing state out of a response.
 */
export interface ProviderFailure {
  kind: ProviderFailureKind
  canAdministratorAct: boolean
  retryAfterSeconds: number | null
}

export class ApiError extends Error {
  status: number
  detail?: string
  /** The `errors` Problem Details extension (`ProblemDetailsMiddleware.cs`) — per-field/per-entry messages for a `validation-failed` (400) response. Undefined for every other error shape. */
  errors?: Record<string, string[]>
  /** specs/043 — set only when the server classified the failure *and* the caller is an administrator. */
  providerFailure?: ProviderFailure

  constructor(
    status: number,
    message: string,
    detail?: string,
    errors?: Record<string, string[]>,
    providerFailure?: ProviderFailure,
  ) {
    super(message)
    this.status = status
    this.detail = detail
    this.errors = errors
    this.providerFailure = providerFailure
  }
}

/**
 * `isAuthFlow` marks calls whose own 401 is a normal outcome for the caller to handle (wrong
 * password on login, an anonymous visitor's session check) rather than the global "kick the
 * user out" behavior below.
 */
export interface ApiFetchInit extends RequestInit {
  isAuthFlow?: boolean
}

async function parseResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    const problem = await response.json().catch(() => undefined)
    throw new ApiError(
      response.status,
      problem?.title ?? 'Request failed',
      problem?.detail,
      problem?.errors,
      problem?.providerFailure,
    )
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

function performFetch(path: string, init: ApiFetchInit): Promise<Response> {
  const accessToken = useAuthStore.getState().accessToken

  return fetch(`${API_BASE_URL}${path}`, {
    ...init,
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      ...init.headers,
    },
  })
}

/** Shared across concurrent 401s so they trigger exactly one `/auth/refresh` call, not one each. */
let refreshPromise: Promise<boolean> | null = null

async function attemptSilentRefresh(): Promise<boolean> {
  refreshPromise ??= (async () => {
    try {
      const response = await fetch(`${API_BASE_URL}/auth/refresh`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
      })
      if (!response.ok) {
        return false
      }

      const result = await parseResponse<{ userId: string | null; accessToken: string | null }>(response)
      if (!result.accessToken || !result.userId) {
        return false
      }

      useAuthStore.getState().setSession(result.accessToken, result.userId)
      return true
    } catch {
      return false
    }
  })()

  try {
    return await refreshPromise
  } finally {
    refreshPromise = null
  }
}

/**
 * JWT-aware fetch wrapper. Attaches the access token and sends/receives the httpOnly
 * refresh-token cookie. On a 401 from a normal (non-auth-flow) call, attempts one silent
 * refresh before giving up and redirecting to login (FR-015/User Story 2).
 */
export async function apiFetch<T>(path: string, init: ApiFetchInit = {}): Promise<T> {
  const response = await performFetch(path, init)

  if (response.status === 401 && !init.isAuthFlow) {
    if (await attemptSilentRefresh()) {
      return parseResponse<T>(await performFetch(path, init))
    }

    useAuthStore.getState().clear()
    window.location.assign('/login')
    throw new ApiError(401, 'Authentication required')
  }

  return parseResponse<T>(response)
}
