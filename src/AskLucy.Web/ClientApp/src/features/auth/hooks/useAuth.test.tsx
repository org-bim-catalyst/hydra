import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import * as authApi from '../api/authApi'
import { SESSION_QUERY_KEY } from './useSession'
import { useLogin, useLogout } from './useAuth'

const server = setupServer(
  http.post('*/api/v1/auth/logout', () => HttpResponse.json(null)),
  http.post('*/api/v1/auth/login', () =>
    HttpResponse.json({
      userId: 'user-1',
      accessToken: 'token',
      expiresAtUtc: '2026-09-20T00:00:00Z',
      requiresTwoFactor: false,
    }),
  ),
  // If the session query actually refetches, this would resolve back to authenticated — the
  // whole point of the fix is that ProtectedRoute never has to wait for this round trip.
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'user-1', roles: [], permissions: [] }),
  ),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderLogout() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  queryClient.setQueryData(SESSION_QUERY_KEY, { authenticated: true, userId: 'user-1', roles: [], permissions: [] })

  const { result } = renderHook(() => useLogout(), {
    wrapper: ({ children }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>,
  })
  return { result, queryClient }
}

describe('useLogout', () => {
  it('sets the session cache to logged-out synchronously on settle, before any refetch resolves', async () => {
    const { result, queryClient } = renderLogout()

    result.current.mutate()

    // Assert as soon as the mutation settles — a refetch may still be in flight behind it, but
    // the cache must already read "authenticated: false" so ProtectedRoute never renders
    // ConsentGate with a session it can no longer actually use.
    await waitFor(() => expect(result.current.isSuccess || result.current.isError).toBe(true))
    expect(queryClient.getQueryData(SESSION_QUERY_KEY)).toEqual({
      authenticated: false,
      userId: null,
      roles: [],
      permissions: [],
    })
  })
})

describe('useLogin', () => {
  it('leaves the session cache authenticated by the time mutateAsync resolves', async () => {
    // Arriving at /login leaves a cached 401 in the session query with a 60s staleTime. The
    // caller navigates to /studio the instant mutateAsync resolves, so if the session refresh
    // is fired-and-forgotten ProtectedRoute reads that stale error and bounces straight back —
    // the "first click does nothing, the second one signs me in" bug.
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    server.use(
      http.get('*/api/v1/auth/session', () => HttpResponse.json({ title: 'No active session' }, { status: 401 })),
    )
    const sessionQuery = queryClient.getQueryCache().build(queryClient, {
      queryKey: SESSION_QUERY_KEY,
      queryFn: authApi.getSession,
    })
    await sessionQuery.fetch().catch(() => undefined)
    expect(queryClient.getQueryData(SESSION_QUERY_KEY)).toBeUndefined()
    // The sign-in itself is what makes the session valid; from here the endpoint answers.
    server.resetHandlers()

    const { result } = renderHook(() => useLogin(), {
      wrapper: ({ children }) => <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>,
    })

    await result.current.mutateAsync({ email: 'someone@example.com', password: 'correct-password' })

    expect(queryClient.getQueryData(SESSION_QUERY_KEY)).toEqual({
      authenticated: true,
      userId: 'user-1',
      roles: [],
      permissions: [],
    })
  })
})
