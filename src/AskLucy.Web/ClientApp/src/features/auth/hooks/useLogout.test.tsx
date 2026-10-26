import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider, useNavigate } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { ProtectedRoute } from '../../../routes/ProtectedRoute'
import { SESSION_QUERY_KEY } from './useSession'
import { useLogout } from './useAuth'

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'user-1', roles: [], permissions: [] }),
  ),
  http.get('*/api/v1/users/me/cookie-consent', () => HttpResponse.json({ requiresReconsent: false, hasConsented: true })),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

/** Mirrors `UserMenu.handleLogout` — the only caller of `useLogout`. */
function LogoutButton() {
  const navigate = useNavigate()
  const logout = useLogout()
  return (
    <button onClick={() => logout.mutate(undefined, { onSettled: () => navigate('/', { replace: true }) })}>
      Log out
    </button>
  )
}

function renderSignedInAtSettings() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/settings',
        element: (
          <ProtectedRoute>
            <div>
              Settings page <LogoutButton />
            </div>
          </ProtectedRoute>
        ),
      },
      { path: '/', element: <div>Landing page</div> },
      { path: '/login', element: <div>Login page</div> },
    ],
    { initialEntries: ['/settings'] },
  )
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return { queryClient, router }
}

describe('useLogout', () => {
  it('leaves the page when the server ends the session', async () => {
    server.use(http.post('*/api/v1/auth/logout', () => new HttpResponse(null, { status: 204 })))
    const { router } = renderSignedInAtSettings()
    await screen.findByText(/Settings page/)

    await userEvent.setup().click(screen.getByText('Log out'))

    await waitFor(() => expect(router.state.location.pathname).toBe('/'))
  })

  // The reported bug: on the Account settings page, signing out flipped the header to "Sign in" but
  // left the page open. The local session must never depend on the server answering.
  it('leaves the page and forgets the session even when the logout request fails', async () => {
    server.use(http.post('*/api/v1/auth/logout', () => HttpResponse.json({ title: 'boom' }, { status: 500 })))
    const { queryClient, router } = renderSignedInAtSettings()
    await screen.findByText(/Settings page/)

    await userEvent.setup().click(screen.getByText('Log out'))

    await waitFor(() => expect(router.state.location.pathname).toBe('/'))
    expect(queryClient.getQueryData(SESSION_QUERY_KEY)).toEqual({
      authenticated: false,
      userId: null,
      roles: [],
      permissions: [],
    })
  })

  it('drops everything else the account cached, keeping the session entry alive for the guards', async () => {
    server.use(http.post('*/api/v1/auth/logout', () => new HttpResponse(null, { status: 204 })))
    const { queryClient } = renderSignedInAtSettings()
    await screen.findByText(/Settings page/)
    queryClient.setQueryData(['chats', 'list'], [{ id: 'chat-1' }])

    await userEvent.setup().click(screen.getByText('Log out'))

    await waitFor(() => expect(queryClient.getQueryData(['chats', 'list'])).toBeUndefined())
    // Still in the cache, not removed: a mounted guard reading it must see the sign-out, not a hole.
    expect(queryClient.getQueryCache().find({ queryKey: SESSION_QUERY_KEY })).toBeDefined()
    expect(queryClient.getQueryData<{ authenticated: boolean }>(SESSION_QUERY_KEY)?.authenticated).toBe(false)
  })
})
