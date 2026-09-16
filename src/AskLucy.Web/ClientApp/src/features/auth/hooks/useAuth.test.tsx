import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { SESSION_QUERY_KEY } from './useSession'
import { useLogout } from './useAuth'

const server = setupServer(
  http.post('*/api/v1/auth/logout', () => HttpResponse.json(null)),
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
