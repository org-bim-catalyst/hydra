import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { AdminNotificationTemplateEditorPage } from './AdminNotificationTemplateEditorPage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: ['User'],
      permissions: ['admin.notifications.view', 'admin.notifications.manage'],
    }),
  ),
  http.get('*/api/v1/admin/notifications/templates/t1', () =>
    HttpResponse.json({
      templateId: 't1',
      type: 'workflow.execution.failed',
      category: 'Workflow',
      channel: 'Email',
      language: 'en',
      name: 'Workflow failed',
      publishedVersionId: null,
      isShippedDefault: false,
      versions: [
        {
          id: 'v1',
          versionNumber: 1,
          status: 'Draft',
          createdAtUtc: '2026-10-05T00:00:00Z',
          createdBy: 'Ada',
          publishedAtUtc: null,
          archivedAtUtc: null,
        },
      ],
      declaredVariables: [
        {
          name: 'workflowName',
          sample: 'your workflow',
          fallback: 'your workflow',
          isStandard: false,
        },
      ],
    }),
  ),
  http.get('*/api/v1/admin/notifications/templates/t1/versions/v1', () =>
    HttpResponse.json({
      id: 'v1',
      templateId: 't1',
      versionNumber: 1,
      status: 'Draft',
      rowVersion: 'AQ==',
      subject: '{{ workflowName }} failed',
      preheader: null,
      greeting: null,
      heading: 'It failed',
      bodyParagraphs: ['Body'],
      actionLabel: 'Open',
      safetyNote: 'Safe',
      footerNote: null,
      title: null,
      message: null,
      usedVariables: ['workflowName'],
      createdAtUtc: '2026-10-05T00:00:00Z',
      publishedAtUtc: null,
      archivedAtUtc: null,
    }),
  ),
  http.post('*/api/v1/admin/notifications/templates/t1/versions/v1/actions/preview', () =>
    HttpResponse.json({
      subject: 'S',
      html: '<p>Hi</p>',
      text: 'Hi',
      title: null,
      message: null,
      actionLabel: null,
      language: 'en',
      direction: 'ltr',
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminNotificationTemplateEditorPage accessibility', () => {
  it('has no axe violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [
        {
          path: '/admin/notifications/templates/:templateId',
          element: <AdminNotificationTemplateEditorPage />,
        },
      ],
      {
        initialEntries: ['/admin/notifications/templates/t1'],
      },
    )
    const { container } = render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )
    await screen.findByLabelText('Subject')
    await screen.findByTitle('Email preview')

    // The preview frame holds a rendered email from the server, a separate document; axe checks this page, not the frame's content.
    expect(await axe(container, { iframes: false })).toHaveNoViolations()
  })
})
