import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { AdminNotificationDeliveriesPage } from './AdminNotificationDeliveriesPage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['User'], permissions: ['admin.notifications.view', 'admin.notifications.manage'] }),
  ),
  http.get('*/api/v1/admin/notifications/deliveries', () =>
    HttpResponse.json({
      items: [
        {
          deliveryId: 'd1', notificationId: 'n1', type: 'workflow.execution.failed', category: 'Workflow', channel: 'Email', status: 'DeadLettered',
          failureKind: 'RetryLimitReached', failureReason: 'Mailbox busy.', providerResponse: '451', attempts: 5, lastAttemptAtUtc: '2026-10-06T10:00:00Z',
          nextAttemptAtUtc: null, recipient: { kind: 'User', userId: 'u1', displayName: 'L. H.', address: 'l•••@bimcatalyst.com' }, correlationId: 'c1',
          retryable: true, notRetryableReason: null,
        },
      ],
      nextCursor: null,
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminNotificationDeliveriesPage accessibility (T157)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter([{ path: '/admin/notifications/deliveries', element: <AdminNotificationDeliveriesPage /> }], {
      initialEntries: ['/admin/notifications/deliveries'],
    })
    const { findByText, container } = render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )
    await findByText('l•••@bimcatalyst.com')

    expect(await axe(container)).toHaveNoViolations()
  })
})
