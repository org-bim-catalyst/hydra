import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { NotificationDetail } from '../api/notificationsApi'
import { NotificationDetails } from './NotificationDetails'

expect.extend(toHaveNoViolations)

const detail: NotificationDetail = {
  id: 'notif-1',
  category: 'Workflow',
  type: 'workflow.failed',
  title: 'فشل تشغيل سير العمل',
  message: 'تعذّر إكمال التشغيل.',
  priority: 'Critical',
  status: 'Delivered',
  language: 'ar',
  createdAtUtc: new Date(Date.now() - 2 * 86_400_000).toISOString(),
  readAtUtc: null,
  expiresAtUtc: null,
  action: { label: 'فتح التشغيل', route: '/workflows/runs/1' },
  relatedItem: { type: 'workflow', id: 'w1', available: true },
  metadata: { runId: 'run-1' },
}

const server = setupServer(
  http.delete('*/api/v1/notifications/notif-1', () =>
    HttpResponse.json({ title: 'تعذّر', detail: 'تعذّر حذف الإشعار.' }, { status: 500 }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderDetails() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <NotificationDetails notification={detail} isLoading={false} isError={false} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('NotificationDetails in Arabic (T191)', () => {
  it('renders in ar/rtl with the notification in its own language and Arabic chrome', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderDetails()

    const title = await screen.findByText('فشل تشغيل سير العمل')
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(container.querySelector('div[lang="ar"]')).toContainElement(title)
    expect(screen.getByText('حرجة')).toBeInTheDocument()
    expect(screen.getByText('تحديد كمقروء')).toBeInTheDocument()
    expect(screen.getByText('فتح التشغيل')).toBeInTheDocument()
    expect(screen.getByText('حذف')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the server-localized error when deleting fails', async () => {
    renderDetails()
    ;(await screen.findByText('حذف')).click()

    expect(await screen.findByText('تعذّر حذف الإشعار.')).toBeInTheDocument()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    const { container } = renderDetails()
    await screen.findByText('فشل تشغيل سير العمل')

    expect(await axe(container)).toHaveNoViolations()
  })
})
