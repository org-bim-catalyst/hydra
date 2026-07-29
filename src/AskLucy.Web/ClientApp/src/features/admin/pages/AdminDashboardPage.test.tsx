import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { DashboardSummary } from '../api/adminApi'
import { AdminDashboardPage } from './AdminDashboardPage'

const summary: DashboardSummary = {
  totalUsers: 50,
  newUsersLast30Days: [{ date: '2026-07-28', newUsers: 4 }],
  activeUsers: 45,
  lockedOutUsers: 5,
  emailConfirmedUsers: 48,
  emailPendingUsers: 2,
  twoFactorEnabledUsers: 10,
  roleDistribution: [
    { roleName: 'Super User', userCount: 1 },
    { roleName: 'Administrator', userCount: 2 },
    { roleName: 'Regular', userCount: 47 },
  ],
}

const server = setupServer(
  http.get('*/api/v1/admin/dashboard/summary', () => HttpResponse.json(summary)),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminDashboardPage', () => {
  it('renders all six core platform metrics (SC-001)', async () => {
    renderPage()

    await waitFor(() => expect(screen.getByText('50')).toBeInTheDocument()) // total users
    expect(screen.getByText('20%')).toBeInTheDocument() // 2FA adoption: 10/50
    expect(screen.getByText('45')).toBeInTheDocument() // active users
    expect(screen.getByText('5')).toBeInTheDocument() // locked out users
    expect(screen.getByText('New users — last 30 days')).toBeInTheDocument()
    expect(screen.getByText('Role distribution')).toBeInTheDocument()
    expect(screen.getByText('Active vs. locked out')).toBeInTheDocument()
    expect(screen.getByText('Email confirmed vs. pending')).toBeInTheDocument()
  })

  it('renders an empty/zero state when there are no users', async () => {
    server.use(
      http.get('*/api/v1/admin/dashboard/summary', () =>
        HttpResponse.json({
          totalUsers: 0,
          newUsersLast30Days: [{ date: '2026-07-28', newUsers: 0 }],
          activeUsers: 0,
          lockedOutUsers: 0,
          emailConfirmedUsers: 0,
          emailPendingUsers: 0,
          twoFactorEnabledUsers: 0,
          roleDistribution: [{ roleName: 'Regular', userCount: 0 }],
        } satisfies DashboardSummary),
      ),
    )

    renderPage()

    await waitFor(() => expect(screen.getByText('No new registrations in this period.')).toBeInTheDocument())
    expect(screen.getAllByText('No registered users yet.').length).toBeGreaterThan(0)
  })
})
