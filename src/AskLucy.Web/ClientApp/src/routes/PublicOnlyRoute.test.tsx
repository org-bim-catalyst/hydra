import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { PublicOnlyRoute } from './PublicOnlyRoute'

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
            path="/"
            element={
              <PublicOnlyRoute>
                <div>Landing content</div>
              </PublicOnlyRoute>
            }
          />
          <Route path="/studio" element={<div>Workspace</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('PublicOnlyRoute (spec.md FR-015, contracts/routing-and-consent-contract.md)', () => {
  it('renders its children for a signed-out visitor (no session cookie -> 401)', async () => {
    server.use(http.get('*/api/v1/auth/session', () => HttpResponse.json({ title: 'No active session' }, { status: 401 })))

    renderAt('/')

    expect(await screen.findByText('Landing content')).toBeInTheDocument()
  })

  it('redirects an already-authenticated visitor straight into the workspace', async () => {
    server.use(
      http.get('*/api/v1/auth/session', () =>
        HttpResponse.json({ authenticated: true, userId: 'user-1', roles: [] }),
      ),
    )

    renderAt('/')

    expect(await screen.findByText('Workspace')).toBeInTheDocument()
    expect(screen.queryByText('Landing content')).not.toBeInTheDocument()
  })

  it('fails open toward the public page when the session check itself errors', async () => {
    server.use(http.get('*/api/v1/auth/session', () => HttpResponse.error()))

    renderAt('/')

    expect(await screen.findByText('Landing content')).toBeInTheDocument()
  })
})
