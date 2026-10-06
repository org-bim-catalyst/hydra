import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { NotificationChannelStatus, NotificationStatistics } from '../api/adminNotificationsApi'
import { AdminNotificationsDashboardPage } from './AdminNotificationsDashboardPage'

const statistics: NotificationStatistics = {
  created: 4210,
  sent: 1180,
  failed: 12,
  deadLettered: 2,
  ambiguous: 1,
  emailSuccessRate: 0.989,
  averageDeliveryLatencyMs: 3400,
  p95DeliveryLatencyMs: 41000,
  retries: 31,
  backlog: { outboxPending: 0, deliveriesDue: 3, oldestDueAgeSeconds: 120 },
  unreadNotifications: 9123,
  byCategory: [{ category: 'Workflow', created: 800, failed: 3 }],
  series: [
    { bucketStartUtc: '2026-09-22T00:00:00Z', created: 610, sent: 170, failed: 1 },
    { bucketStartUtc: '2026-09-23T00:00:00Z', created: 500, sent: 120, failed: 2 },
  ],
}

const channels: NotificationChannelStatus[] = [
  { channel: 'Email', enabled: true, provider: 'SMTP', health: 'Degraded', checkedAtUtc: '2026-10-06T10:00:00Z', detail: 'The mail server probe failed (TimeoutException).', sendLimitPerMinute: 60 },
  { channel: 'InApp', enabled: true, provider: 'SignalR', health: 'Healthy', checkedAtUtc: '2026-10-06T10:00:00Z', detail: null, sendLimitPerMinute: null },
]

const requestedRanges: URLSearchParams[] = []
const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['User'], permissions: ['admin.notifications.view'] }),
  ),
  http.get('*/api/v1/admin/notifications/statistics', ({ request }) => {
    requestedRanges.push(new URL(request.url).searchParams)
    return HttpResponse.json(statistics)
  }),
  http.get('*/api/v1/admin/notifications/channels', () => HttpResponse.json(channels)),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  requestedRanges.length = 0
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([{ path: '/admin/notifications/dashboard', element: <AdminNotificationsDashboardPage /> }], {
    initialEntries: ['/admin/notifications/dashboard'],
  })
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

describe('AdminNotificationsDashboardPage (T157, specs/067 US6)', () => {
  it('shows the counts, the speed and what is waiting', async () => {
    renderPage()

    expect(await screen.findByText('4210')).toBeInTheDocument()
    expect(screen.getByText('98.9%')).toBeInTheDocument()
    expect(screen.getByText('3.4 s')).toBeInTheDocument()
    expect(screen.getByText('41.0 s')).toBeInTheDocument()
    expect(screen.getByText('1 with an unknown outcome')).toBeInTheDocument()
    expect(screen.getByText('oldest 2 min')).toBeInTheDocument()
    expect(screen.getByText('Workflow: 800 created, 3 failed')).toBeInTheDocument()
  })

  it('shows each channel with its health, and says why one is not healthy', async () => {
    renderPage()

    expect(await screen.findByText('Email (SMTP): Degraded')).toBeInTheDocument()
    expect(screen.getByText('In-app (SignalR): Healthy')).toBeInTheDocument()
  })

  it('describes the chart in words, for a screen reader', async () => {
    renderPage()

    expect(await screen.findByRole('img', { name: 'Notifications per day: 1110 created, 290 sent, 3 failed' })).toBeInTheDocument()
  })

  it('asks again for a different range when it is changed', async () => {
    renderPage()
    await screen.findByText('4210')
    const before = requestedRanges.length

    fireEvent.click(screen.getByRole('button', { name: '30 days' }))

    await waitFor(() => expect(requestedRanges.length).toBeGreaterThan(before))
    const last = requestedRanges[requestedRanges.length - 1]
    const days = (new Date(last.get('to')!).getTime() - new Date(last.get('from')!).getTime()) / 86_400_000
    expect(Math.round(days)).toBe(30)
  })

  it('shows an error with a retry when the numbers cannot be loaded, instead of zeros', async () => {
    server.use(http.get('*/api/v1/admin/notifications/statistics', () => HttpResponse.json({ title: 'Failed', detail: 'The database is unavailable.', status: 500 }, { status: 500 })))
    renderPage()

    expect(await screen.findByText('The database is unavailable.')).toBeInTheDocument()
    expect(screen.queryByText('4210')).not.toBeInTheDocument()
    server.use(http.get('*/api/v1/admin/notifications/statistics', () => HttpResponse.json(statistics)))
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(await screen.findByText('4210')).toBeInTheDocument()
  })

  it('shows an error when the channels cannot be loaded', async () => {
    server.use(http.get('*/api/v1/admin/notifications/channels', () => HttpResponse.json({ title: 'Failed', detail: 'Health is unavailable.', status: 500 }, { status: 500 })))
    renderPage()

    expect(await screen.findByText('Health is unavailable.')).toBeInTheDocument()
  })
})
