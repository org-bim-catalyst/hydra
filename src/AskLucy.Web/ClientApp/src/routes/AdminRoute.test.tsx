import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { AdminRoute } from './AdminRoute'

const server = setupServer()

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderAt(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route
            path="/admin"
            element={
              <AdminRoute>
                <div>Admin console</div>
              </AdminRoute>
            }
          />
          <Route path="/login" element={<div>Login page</div>} />
          <Route path="/studio" element={<div>Workspace</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminRoute (UX affordance only — server enforces the real policy, FR-017)', () => {
  it('redirects to /login when there is no active session', async () => {
    server.use(http.get('*/api/v1/auth/session', () => HttpResponse.json({ title: 'No active session' }, { status: 401 })))

    renderAt('/admin')

    expect(await screen.findByText('Login page')).toBeInTheDocument()
  })

  it('redirects a signed-in non-admin to /studio', async () => {
    server.use(
      http.get('*/api/v1/auth/session', () =>
        HttpResponse.json({ authenticated: true, userId: 'user-1', roles: ['Regular'] }),
      ),
    )

    renderAt('/admin')

    expect(await screen.findByText('Workspace')).toBeInTheDocument()
  })

  it('renders the admin content for an Administrator', async () => {
    server.use(
      http.get('*/api/v1/auth/session', () =>
        HttpResponse.json({ authenticated: true, userId: 'user-1', roles: ['Administrator'] }),
      ),
    )

    renderAt('/admin')

    expect(await screen.findByText('Admin console')).toBeInTheDocument()
  })

  it('renders the admin content for a Super User', async () => {
    server.use(
      http.get('*/api/v1/auth/session', () =>
        HttpResponse.json({ authenticated: true, userId: 'user-1', roles: ['Super User'] }),
      ),
    )

    renderAt('/admin')

    expect(await screen.findByText('Admin console')).toBeInTheDocument()
  })
})
