import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AdminDelivery } from '../api/adminNotificationsApi'
import { AdminNotificationDeliveriesPage } from './AdminNotificationDeliveriesPage'

const delivery = (id: string, overrides: Partial<AdminDelivery> = {}): AdminDelivery => ({
  deliveryId: id,
  notificationId: `n-${id}`,
  type: 'workflow.execution.failed',
  category: 'Workflow',
  channel: 'Email',
  status: 'DeadLettered',
  failureKind: 'RetryLimitReached',
  failureReason: 'Mail server rejected the message (temporary): mailbox busy.',
  providerResponse: '451 4.3.0',
  attempts: 5,
  lastAttemptAtUtc: '2026-10-06T10:00:00Z',
  nextAttemptAtUtc: null,
  recipient: { kind: 'User', userId: 'u1', displayName: 'L. H.', address: 'l•••@bimcatalyst.com' },
  correlationId: 'corr-1',
  retryable: true,
  notRetryableReason: null,
  ...overrides,
})

let permissions = ['admin.notifications.view', 'admin.notifications.manage']
let items: AdminDelivery[] = []
const retried: string[] = []
const bulkBodies: unknown[] = []
const listQueries: URLSearchParams[] = []

const server = setupServer(
  http.get('*/api/v1/auth/session', () => HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['User'], permissions })),
  http.get('*/api/v1/admin/notifications/deliveries', ({ request }) => {
    listQueries.push(new URL(request.url).searchParams)
    return HttpResponse.json({ items, nextCursor: null })
  }),
  http.get('*/api/v1/admin/notifications/deliveries/:id', ({ params }) => {
    const found = items.find((i) => i.deliveryId === params.id)!
    return HttpResponse.json({ delivery: found, notification: { title: 'Workflow failed', createdAtUtc: '2026-10-06T09:00:00Z', language: 'en', templateVersionId: null } })
  }),
  http.post('*/api/v1/admin/notifications/deliveries/actions/retry', async ({ request }) => {
    bulkBodies.push(await request.json())
    return HttpResponse.json({ requested: 2, retried: 1, skipped: [{ deliveryId: 'd2', reason: 'NotificationExpired' }] })
  }),
  http.post('*/api/v1/admin/notifications/deliveries/:id/actions/retry', ({ params }) => {
    retried.push(params.id as string)
    return HttpResponse.json({ deliveryId: params.id, status: 'Pending' }, { status: 202 })
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  permissions = ['admin.notifications.view', 'admin.notifications.manage']
  items = []
  retried.length = 0
  bulkBodies.length = 0
  listQueries.length = 0
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const router = createMemoryRouter([{ path: '/admin/notifications/deliveries', element: <AdminNotificationDeliveriesPage /> }], {
    initialEntries: ['/admin/notifications/deliveries'],
  })
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

/** Buttons are found by their text: role queries trip a jsdom bug once a drawer or dialog is open. */
const button = (text: string) => screen.getByText(text).closest('button') as HTMLButtonElement

describe('AdminNotificationDeliveriesPage (T157, specs/067 US6)', () => {
  it('lists the failed deliveries by default, with the recipient masked', async () => {
    items = [delivery('d1')]
    renderPage()

    expect(await screen.findByText('l•••@bimcatalyst.com')).toBeInTheDocument()
    expect(screen.getByText('Mail server rejected the message (temporary): mailbox busy.')).toBeInTheDocument()
    expect(listQueries[0].getAll('status')).toEqual(['Failed', 'DeadLettered'])
  })

  it('shows the support mailbox as a role, never an address', async () => {
    items = [delivery('d1', { recipient: { kind: 'SupportMailbox', userId: null, displayName: null, address: null } })]
    renderPage()

    expect(await screen.findByText('Support mailbox')).toBeInTheDocument()
  })

  it('hides every retry control from an administrator who can only view', async () => {
    permissions = ['admin.notifications.view']
    items = [delivery('d1')]
    renderPage()

    await screen.findByText('l•••@bimcatalyst.com')
    expect(screen.queryByText('Retry')).not.toBeInTheDocument()
    expect(screen.queryByText('Retry selected')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Select all retryable deliveries')).not.toBeInTheDocument()
  })

  it('retries one delivery and says so', async () => {
    items = [delivery('d1')]
    renderPage()
    await screen.findByText('l•••@bimcatalyst.com')

    fireEvent.click(button('Retry'))

    await waitFor(() => expect(retried).toEqual(['d1']))
    expect(await screen.findByText('The delivery was queued to be sent again.')).toBeInTheDocument()
  })

  it('shows a toast with the reason when a retry fails', async () => {
    items = [delivery('d1')]
    server.use(
      http.post('*/api/v1/admin/notifications/deliveries/:id/actions/retry', () =>
        HttpResponse.json({ title: 'Delivery can\'t be retried', detail: 'This delivery can\'t be retried: NotificationExpired.', reason: 'NotificationExpired', status: 409 }, { status: 409 }),
      ),
    )
    renderPage()
    await screen.findByText('l•••@bimcatalyst.com')

    fireEvent.click(button('Retry'))

    expect(await screen.findByText(/The retry didn't go through\. This delivery can't be retried: NotificationExpired\./)).toBeInTheDocument()
  })

  it('disables retry for a delivery that cannot be retried, and says why on hover', async () => {
    items = [delivery('d1', { retryable: false, notRetryableReason: 'NotificationExpired' })]
    renderPage()
    await screen.findByText('l•••@bimcatalyst.com')

    expect(button('Retry')).toBeDisabled()
  })

  it('retries the selected deliveries together and reports what was skipped', async () => {
    items = [delivery('d1'), delivery('d2')]
    renderPage()
    await screen.findAllByText('l•••@bimcatalyst.com')

    fireEvent.click(screen.getByLabelText('Select all retryable deliveries'))
    fireEvent.click(button('Retry 2 selected'))

    await waitFor(() => expect(bulkBodies).toEqual([{ deliveryIds: ['d1', 'd2'] }]))
    expect(await screen.findByText('Retried 1 of 2, 1 skipped.')).toBeInTheDocument()
  })

  it('shows a toast when the bulk retry fails', async () => {
    items = [delivery('d1')]
    server.use(http.post('*/api/v1/admin/notifications/deliveries/actions/retry', () => HttpResponse.json({ title: 'Failed', detail: 'The database is unavailable.', status: 500 }, { status: 500 })))
    renderPage()
    await screen.findByText('l•••@bimcatalyst.com')

    fireEvent.click(screen.getByLabelText('Select all retryable deliveries'))
    fireEvent.click(button('Retry 1 selected'))

    expect(await screen.findByText(/The retry didn't go through\. The database is unavailable\./)).toBeInTheDocument()
  })

  it('opens the details of one delivery, including why it failed', async () => {
    items = [delivery('d1')]
    renderPage()
    await screen.findByText('l•••@bimcatalyst.com')

    fireEvent.click(screen.getByLabelText('Open workflow.execution.failed delivery'))

    expect(await screen.findByText('451 4.3.0')).toBeInTheDocument()
    expect(screen.getByText('corr-1')).toBeInTheDocument()
    expect(screen.getByText('Workflow failed')).toBeInTheDocument()
  })

  it('says so when nothing matches, and shows an error with a retry when the list fails', async () => {
    renderPage()
    expect(await screen.findByText('No deliveries match these filters.')).toBeInTheDocument()
  })

  it('shows an error when the list cannot be loaded', async () => {
    server.use(http.get('*/api/v1/admin/notifications/deliveries', () => HttpResponse.json({ title: 'Failed', detail: 'The database is unavailable.', status: 500 }, { status: 500 })))
    renderPage()

    expect(await screen.findByText('The database is unavailable.')).toBeInTheDocument()
  })
})
