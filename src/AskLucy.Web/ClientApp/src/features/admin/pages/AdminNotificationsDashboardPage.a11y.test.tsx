import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { AdminNotificationsDashboardPage } from './AdminNotificationsDashboardPage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['User'], permissions: ['admin.notifications.view'] }),
  ),
  http.get('*/api/v1/admin/notifications/statistics', () =>
    HttpResponse.json({
      created: 10, sent: 5, failed: 1, deadLettered: 0, ambiguous: 0, emailSuccessRate: 0.9, averageDeliveryLatencyMs: 1200, p95DeliveryLatencyMs: 3000, retries: 1,
      backlog: { outboxPending: 0, deliveriesDue: 0, oldestDueAgeSeconds: 0 }, unreadNotifications: 4,
      byCategory: [{ category: 'Workflow', created: 10, failed: 1 }],
      series: [{ bucketStartUtc: '2026-09-22T00:00:00Z', created: 10, sent: 5, failed: 1 }],
    }),
  ),
  http.get('*/api/v1/admin/notifications/channels', () =>
    HttpResponse.json([
      { channel: 'Email', enabled: true, provider: 'SMTP', health: 'Healthy', checkedAtUtc: null, detail: 'STARTTLS ok', sendLimitPerMinute: 60 },
      { channel: 'InApp', enabled: true, provider: 'SignalR', health: 'Healthy', checkedAtUtc: null, detail: null, sendLimitPerMinute: null },
    ]),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminNotificationsDashboardPage accessibility (T157)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter([{ path: '/admin/notifications/dashboard', element: <AdminNotificationsDashboardPage /> }], {
      initialEntries: ['/admin/notifications/dashboard'],
    })
    const { findByText, container } = render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )
    await findByText('In-app (SignalR): Healthy')
    await findByText('Workflow: 10 created, 1 failed')

    expect(await axe(container)).toHaveNoViolations()
  })
})
