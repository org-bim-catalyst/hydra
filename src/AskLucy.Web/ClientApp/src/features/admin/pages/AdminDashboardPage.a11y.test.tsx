import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { DashboardSummary } from '../api/adminApi'
import { AdminDashboardPage } from './AdminDashboardPage'

expect.extend(toHaveNoViolations)

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

const server = setupServer(http.get('*/api/v1/admin/dashboard/summary', () => HttpResponse.json(summary)))

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminDashboardPage accessibility', () => {
  it('has no automatically detectable a11y violations (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminDashboardPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('New users — last 30 days')

    const results = await axe(container);
    expect(results).toHaveNoViolations();
  })
})
