import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { AdminNotificationTemplatesPage } from './AdminNotificationTemplatesPage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: ['User'],
      permissions: ['admin.notifications.view'],
    }),
  ),
  http.get('*/api/v1/admin/notifications/templates', () =>
    HttpResponse.json([
      {
        templateId: 't1',
        type: 'workflow.execution.failed',
        category: 'Workflow',
        channel: 'Email',
        language: 'en',
        name: 'Workflow failed',
        publishedVersion: { id: 'v1', versionNumber: 1, publishedAtUtc: null },
        hasDraft: true,
      },
    ]),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminNotificationTemplatesPage accessibility', () => {
  it('has no axe violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [{ path: '/admin/notifications/templates', element: <AdminNotificationTemplatesPage /> }],
      {
        initialEntries: ['/admin/notifications/templates'],
      },
    )
    const { container } = render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )
    await screen.findByText('Workflow failed')

    expect(await axe(container)).toHaveNoViolations()
  })
})
