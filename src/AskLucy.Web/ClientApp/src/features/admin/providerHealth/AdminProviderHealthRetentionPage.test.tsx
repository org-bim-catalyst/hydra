import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { AdminProviderHealthRetentionPage } from './AdminProviderHealthRetentionPage'

const RETENTION_URL = '*/api/v1/admin/provider-health/retention'

const stored = {
  retentionDays: 30,
  intervalMinutes: 2,
  source: 'Stored',
  rowVersion: 'rv-1',
}

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: [],
      permissions: ['admin.operational-failures.manage'],
    }),
  ),
  // The admin shell's nav badge (specs/074) polls this for anyone who can view the area.
  http.get('*/api/v1/admin/operational-failures/summary', () =>
    HttpResponse.json({ unacknowledgedCriticalRootCauses: 0 }),
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
  it('shows the stored retention window and the check interval', async () => {
    server.use(http.get(RETENTION_URL, () => HttpResponse.json(stored)))

    renderPage()

    expect(await screen.findByRole('spinbutton', { name: /days kept/i })).toHaveValue(30)
    expect(screen.getByText(/every 2 minutes/)).toBeInTheDocument()
  })

  it('saves a new window with the row version it was read at', async () => {
    let ifMatch: string | null = null
    server.use(
      http.get(RETENTION_URL, () => HttpResponse.json(stored)),
      http.put(RETENTION_URL, async ({ request }) => {
        ifMatch = request.headers.get('If-Match')
        return HttpResponse.json({ ...stored, retentionDays: 14, rowVersion: 'rv-2' })
      }),
    )

    renderPage()

    const input = await screen.findByRole('spinbutton', { name: /days kept/i })
    fireEvent.change(input, { target: { value: '14' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(/retention window saved/i)).toBeInTheDocument()
    expect(ifMatch).toBe('rv-1')
  })

  it('refuses an out-of-range value before sending it', async () => {
    server.use(http.get(RETENTION_URL, () => HttpResponse.json(stored)))

    renderPage()

    const input = await screen.findByRole('spinbutton', { name: /days kept/i })
    fireEvent.change(input, { target: { value: '0' } })

    expect(screen.getByText(/Enter a whole number from 1 to 365/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('surfaces a stale edit as a conflict with a reload action', async () => {
    server.use(
      http.get(RETENTION_URL, () => HttpResponse.json(stored)),
      http.put(RETENTION_URL, () =>
        HttpResponse.json(
          {
            title: 'Conflict',
            detail: 'The retention window was changed by someone else. Reload it and try again.',
          },
          { status: 409 },
        ),
      ),
    )

    renderPage()

    const input = await screen.findByRole('spinbutton', { name: /days kept/i })
    fireEvent.change(input, { target: { value: '14' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(/changed by someone else/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })

  it('is read-only for a user without the manage permission', async () => {
    server.use(
      http.get('*/api/v1/auth/session', () =>
        HttpResponse.json({
          authenticated: true,
          userId: 'viewer-1',
          roles: [],
          permissions: ['admin.operational-failures.view'],
        }),
      ),
      http.get(RETENTION_URL, () => HttpResponse.json(stored)),
    )

    renderPage()

    expect(await screen.findByRole('spinbutton', { name: /days kept/i })).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull()
    expect(screen.getByText(/can view this value but not change it/i)).toBeInTheDocument()
  })

  it('shows a visible error with a retry when the value cannot be loaded', async () => {
    server.use(
      http.get(RETENTION_URL, () => HttpResponse.json({ title: 'Server error' }, { status: 500 })),
    )

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be loaded|Server error/)
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
