import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { NotificationTemplateSummary } from '../api/adminNotificationTemplatesApi'
import { AdminNotificationTemplatesPage } from './AdminNotificationTemplatesPage'

expect.extend(toHaveNoViolations)

const rows: NotificationTemplateSummary[] = [
  {
    templateId: 't1',
    type: 'workflow.execution.failed',
    category: 'Workflow',
    channel: 'Email',
    language: 'ar',
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
  http.get('*/api/v1/admin/notifications/templates', () =>
    failing
      ? HttpResponse.json(
          { title: 'Server error', detail: 'تعذّر تحميل القائمة.' },
          { status: 500 },
        )
      : HttpResponse.json(rows),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  failing = false
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  const router = createMemoryRouter(
    [
      {
        path: '/admin/notifications/templates',
        element: (
          <LocalizedSurface scope="page">
            <AdminNotificationTemplatesPage />
          </LocalizedSurface>
        ),
      },
    ],
    { initialEntries: ['/admin/notifications/templates'] },
  )
  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

describe('AdminNotificationTemplatesPage in Arabic (T218)', () => {
  it('renders in ar/rtl with Arabic copy; template names, types and language codes are data', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    expect(await screen.findByText('قوالب الإشعارات', { selector: 'h5' })).toBeInTheDocument()
    expect(document.documentElement).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('صياغة كل إشعار، بكل لغة')).toBeInTheDocument()

    const link = await screen.findByRole('link', { name: 'Workflow failed (email)' })
    expect(link).toHaveAttribute('href', '/admin/notifications/templates/t1')
    expect(screen.getAllByText('workflow.execution.failed')[0]).toHaveAttribute('dir', 'ltr')
    expect(screen.getByText('ar')).toHaveAttribute('dir', 'ltr')

    expect(screen.getByRole('table', { name: 'قوالب الإشعارات' })).toBeInTheDocument()
    expect(screen.getByText('القالب')).toBeInTheDocument()
    expect(screen.getByText('المنشور')).toBeInTheDocument()
    expect(screen.getByText('الإصدار 2')).toBeInTheDocument()
    expect(screen.getByText('لا يوجد')).toBeInTheDocument()
    expect(screen.getByText('مسودة بانتظار النشر')).toBeInTheDocument()
    expect(screen.getByText('البريد الإلكتروني')).toBeInTheDocument()
    expect(screen.getByText('داخل التطبيق')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the server message as returned with an Arabic retry button when the list fails', async () => {
    failing = true
    renderPage()

    expect(await screen.findByText('تعذّر تحميل القائمة.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument()
  })

  it('says in Arabic when nothing matches', async () => {
    server.use(http.get('*/api/v1/admin/notifications/templates', () => HttpResponse.json([])))
    renderPage()
    await waitFor(() => expect(screen.getByText('لا توجد قوالب مطابقة.')).toBeInTheDocument())
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage()
      await screen.findByRole('link', { name: 'Workflow failed (email)' })
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})
