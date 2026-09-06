import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { ErrorPage } from '../components/ErrorPage'
import { ProtectedRoute } from './ProtectedRoute'

const server = setupServer(
  http.get('*/api/v1/users/me/cookie-consent', () =>
    HttpResponse.json({ requiresReconsent: false }),
  ),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderAt(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [
      {
        path: '/studio',
        element: (
          <ProtectedRoute>
            <div>Workspace</div>
          </ProtectedRoute>
        ),
        errorElement: <ErrorPage />,
      },
      { path: '/login', element: <div>Login page</div> },
    ],
    { initialEntries: [path] },
  )

  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

describe('ProtectedRoute', () => {
  it('redirects to /login when there is no active session', async () => {
    server.use(http.get('*/api/v1/auth/session', () => HttpResponse.json({ title: 'No active session' }, { status: 401 })))

    renderAt('/studio')

    expect(await screen.findByText('Login page')).toBeInTheDocument()
  })

  it('renders the protected content for an authenticated session', async () => {
    server.use(
      http.get('*/api/v1/auth/session', () =>
        HttpResponse.json({ authenticated: true, userId: 'user-1', roles: [] }),
      ),
    )

    renderAt('/studio')

    expect(await screen.findByText('Workspace')).toBeInTheDocument()
  })

  it('surfaces a real backend failure via the error boundary instead of silently redirecting', async () => {
    server.use(http.get('*/api/v1/auth/session', () => HttpResponse.json({ title: 'Server error' }, { status: 500 })))

    renderAt('/studio')

    expect(await screen.findByText('Something went wrong')).toBeInTheDocument()
    expect(screen.queryByText('Login page')).not.toBeInTheDocument()
  })
})
