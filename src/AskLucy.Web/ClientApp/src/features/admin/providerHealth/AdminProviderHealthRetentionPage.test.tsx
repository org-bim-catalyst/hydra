import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { AdminProviderHealthRetentionPage } from './AdminProviderHealthRetentionPage'

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions: [] }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <AdminProviderHealthRetentionPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminProviderHealthRetentionPage', () => {
  it('shows the configured retention window and the check interval', async () => {
    server.use(
      http.get('*/api/v1/admin/provider-health/retention', () =>
        HttpResponse.json({ retentionDays: 30, intervalMinutes: 2 }),
      ),
    )

    renderPage()

    expect(await screen.findByText(/30 days/)).toBeInTheDocument()
    expect(screen.getByText(/every 2 minutes/)).toBeInTheDocument()
  })

  it('shows a visible error with a retry when the value cannot be loaded', async () => {
    server.use(
      http.get('*/api/v1/admin/provider-health/retention', () =>
        HttpResponse.json({ title: 'Server error' }, { status: 500 }),
      ),
    )

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be loaded|Server error/)
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('offers no edit control: the value is read-only', async () => {
    server.use(
      http.get('*/api/v1/admin/provider-health/retention', () =>
        HttpResponse.json({ retentionDays: 30, intervalMinutes: 2 }),
      ),
    )

    renderPage()
    await screen.findByText(/30 days/)

    expect(screen.queryByRole('textbox')).toBeNull()
    expect(screen.queryByRole('spinbutton')).toBeNull()
    expect(screen.queryByRole('button', { name: /save/i })).toBeNull()
  })
})
