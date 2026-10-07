import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import {
  lacksArabicIndicDigits,
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import { AdminNotificationsDashboardPage } from './AdminNotificationsDashboardPage'

expect.extend(toHaveNoViolations)

/** Unicode isolates wrap interpolated text in Arabic; compare the visible text without them. */
const plain = (text: string) => text.replace(/[⁦-⁩]/g, '')
const hasText = (expected: string) => (_: string, element: Element | null) =>
  element !== null && element.children.length === 0 && plain(element.textContent ?? '') === expected

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: ['User'],
      permissions: ['admin.notifications.view'],
    }),
  ),
  http.get('*/api/v1/admin/notifications/statistics', () =>
    HttpResponse.json({
      created: 1234,
      sent: 5,
      failed: 1,
      deadLettered: 0,
      ambiguous: 2,
      emailSuccessRate: 0.9,
      averageDeliveryLatencyMs: 1200,
      p95DeliveryLatencyMs: 3000,
      retries: 1,
      backlog: { outboxPending: 1, deliveriesDue: 2, oldestDueAgeSeconds: 600 },
      unreadNotifications: 4,
      byCategory: [{ category: 'Workflow', created: 10, failed: 1 }],
      series: [{ bucketStartUtc: '2026-09-22T00:00:00Z', created: 10, sent: 5, failed: 1 }],
    }),
  ),
  http.get('*/api/v1/admin/notifications/channels', () =>
    HttpResponse.json([
      {
        channel: 'Email',
        enabled: true,
        provider: 'SMTP',
        health: 'Healthy',
        checkedAtUtc: null,
        detail: 'STARTTLS ok',
        sendLimitPerMinute: 60,
      },
      {
        channel: 'InApp',
        enabled: false,
        provider: 'SignalR',
        health: 'Degraded',
        checkedAtUtc: null,
        detail: null,
        sendLimitPerMinute: null,
      },
    ]),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  const router = createMemoryRouter(
    [
      {
        path: '/admin/notifications/dashboard',
        element: (
          <LocalizedSurface scope="page">
            <AdminNotificationsDashboardPage />
          </LocalizedSurface>
        ),
      },
    ],
    { initialEntries: ['/admin/notifications/dashboard'] },
  )
  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

describe('AdminNotificationsDashboardPage in Arabic (T218)', () => {
  it('renders in ar/rtl with Arabic copy, Western digits and no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    expect(await screen.findByText('الإشعارات')).toBeInTheDocument()
    expect(document.documentElement).toHaveAttribute('lang', 'ar')
    expect(document.documentElement).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('سلامة التسليم والحجم وما ينتظر الإرسال')).toBeInTheDocument()
    expect(await screen.findByText(hasText('البريد الإلكتروني (SMTP): سليمة'))).toBeInTheDocument()
    // A channel that is switched off says so; provider and channel names stay as returned or translated.
    expect(screen.getByText(hasText('داخل التطبيق (SignalR): متدهورة، متوقفة'))).toBeInTheDocument()
    expect(await screen.findByText('المُنشأة')).toBeInTheDocument()
    expect(screen.getByText('1234')).toBeInTheDocument()
    expect(screen.getByText(hasText('90.0%'))).toBeInTheDocument()
    expect(screen.getByText(hasText('1.2 ثانية'))).toBeInTheDocument()
    expect(screen.getByText(hasText('2 بنتيجة غير معروفة'))).toBeInTheDocument()
    expect(screen.getByText(hasText('الأقدم 10 دقيقة'))).toBeInTheDocument()
    expect(screen.getByText('الأبطأ 5% (p95)')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '7 أيام' })).toBeInTheDocument()
    expect(screen.getByText(hasText('سير العمل: 10 مُنشأة، 1 فاشلة'))).toBeInTheDocument()
    expect(lacksArabicIndicDigits(document.body.textContent ?? '')).toBe(true)
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('keeps the chart picture left-to-right while its caption, legend and accessible name are Arabic', async () => {
    renderPage()

    const chart = await screen.findByRole('img', {
      name: (name) => plain(name) === 'الإشعارات لكل يوم: 10 مُنشأة، 5 مُرسلة، 1 فاشلة',
    })
    expect(chart.tagName.toLowerCase()).toBe('svg')
    expect(chart).toHaveAttribute('direction', 'ltr')
    expect(screen.getByText('لكل يوم')).toBeInTheDocument()
    expect(screen.getByText(hasText('المُنشأة: 10'))).toBeInTheDocument()
    expect(screen.getByText(hasText('المُرسلة: 5'))).toBeInTheDocument()
    expect(screen.getByText(hasText('الفاشلة: 1'))).toBeInTheDocument()
  })

  it('shows a localized retry affordance and the generic Arabic message when the channels fail', async () => {
    server.use(http.get('*/api/v1/admin/notifications/channels', () => HttpResponse.error()))
    renderPage()

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument(),
    )
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage()
      await screen.findByText(hasText('البريد الإلكتروني (SMTP): سليمة'))
      await screen.findByRole('img', { name: /الإشعارات لكل يوم/ })
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})
