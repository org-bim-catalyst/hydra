import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { NotificationTemplateSummary } from '../api/adminNotificationTemplatesApi'
import { AdminNotificationTemplatesPage } from './AdminNotificationTemplatesPage'

const rows: NotificationTemplateSummary[] = [
  {
    templateId: 't1',
    type: 'workflow.execution.failed',
    category: 'Workflow',
    channel: 'Email',
    language: 'en',
    name: 'Workflow failed (email)',
    publishedVersion: { id: 'v1', versionNumber: 2, publishedAtUtc: '2026-10-01T00:00:00Z' },
    hasDraft: true,
  },
  {
    templateId: 't2',
    type: 'workflow.execution.failed',
    category: 'Workflow',
    channel: 'InApp',
    language: 'en',
    name: 'Workflow failed (in-app)',
    publishedVersion: null,
    hasDraft: false,
  },
]

const requestedUrls: string[] = []
let failing = false

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: ['User'],
      permissions: ['admin.notifications.view'],
    }),
  ),
  http.get('*/api/v1/admin/notifications/templates', ({ request }) => {
    requestedUrls.push(request.url)
    return failing
      ? HttpResponse.json({ title: 'Server error', detail: 'The list failed.' }, { status: 500 })
      : HttpResponse.json(rows)
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  requestedUrls.length = 0
  failing = false
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const router = createMemoryRouter(
    [{ path: '/admin/notifications/templates', element: <AdminNotificationTemplatesPage /> }],
    {
      initialEntries: ['/admin/notifications/templates'],
    },
  )
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

describe('AdminNotificationTemplatesPage (specs/067 US7)', () => {
  it('lists templates with what is published and whether a draft waits, linking to the editor', async () => {
    renderPage()

    const link = await screen.findByRole('link', { name: 'Workflow failed (email)' })
    expect(link).toHaveAttribute('href', '/admin/notifications/templates/t1')
    expect(screen.getByText('v2')).toBeInTheDocument()
    expect(screen.getByText('Draft waiting')).toBeInTheDocument()
    expect(screen.getByText('None')).toBeInTheDocument()
  })

  it('sends the filters it is given', async () => {
    renderPage()
    await screen.findByText('Workflow failed (email)')

    fireEvent.change(screen.getByLabelText('Language'), { target: { value: 'ar' } })

    await waitFor(() => expect(requestedUrls.at(-1)).toContain('language=ar'))
  })

  it('shows a failed load with a retry, never an empty list', async () => {
    failing = true
    renderPage()

    expect(await screen.findByText('The list failed.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
