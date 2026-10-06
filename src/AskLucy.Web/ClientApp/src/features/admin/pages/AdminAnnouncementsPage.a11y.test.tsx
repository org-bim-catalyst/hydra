import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { AdminAnnouncementsPage } from './AdminAnnouncementsPage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['User'], permissions: ['admin.notifications.view', 'admin.notifications.manage'] }),
  ),
  http.get('*/api/v1/admin/notifications/announcements', () =>
    HttpResponse.json({
      items: [
        {
          id: 'a1', kind: 'Maintenance', title: 'Scheduled maintenance', audience: 'AllActiveUsers', targetRoles: [], isCritical: true,
          endsAtUtc: null, publishedAtUtc: '2026-10-06T10:00:00Z', publishedBy: 'Ada Lovelace', recipientCount: 10, fanOutStatus: 'Completed',
          emailQueued: 0, emailSent: 10, emailExpired: 0,
        },
      ],
      nextCursor: null,
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter([{ path: '/admin/notifications/announcements', element: <AdminAnnouncementsPage /> }], {
    initialEntries: ['/admin/notifications/announcements'],
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

describe('AdminAnnouncementsPage accessibility (T157)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const { findByText, container } = renderPage()
    await findByText('Scheduled maintenance')

    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no violations with the publish dialog open', async () => {
    const { findByText, container } = renderPage()
    await findByText('Scheduled maintenance')
    fireEvent.click((await findByText('New announcement')).closest('button') as HTMLButtonElement)
    await screen.findByLabelText(/^Title/)

    expect(await axe(document.body)).toHaveNoViolations()
    expect(container).toBeTruthy()
  })
})
