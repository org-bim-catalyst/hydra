import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
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
import type { DashboardSummary } from '../api/adminApi'
import { AdminDashboardPage } from './AdminDashboardPage'

expect.extend(toHaveNoViolations)

const summary: DashboardSummary = {
  totalUsers: 50,
  newUsersLast30Days: [{ date: '2026-07-28', newUsers: 4 }],
  activeUsers: 45,
  lockedOutUsers: 5,
  emailConfirmedUsers: 48,
  emailPendingUsers: 2,
  twoFactorEnabledUsers: 10,
  roleDistribution: [
    { roleName: 'Super User', userCount: 1 },
    { roleName: 'Administrator', userCount: 2 },
    { roleName: 'User', userCount: 47 },
  ],
}

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions: [] }),
  ),
  http.get('*/api/v1/admin/dashboard/summary', () => HttpResponse.json(summary)),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminDashboardPage in Arabic (T211)', () => {
  it('renders in ar/rtl with Arabic title, tiles and chart captions and no catalog fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderPage()

    expect(await screen.findByText('إجمالي المستخدمين')).toBeInTheDocument()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('لوحة تحكم المسؤول')).toBeInTheDocument()
    expect(screen.getByText('نسبة تفعيل 2FA')).toBeInTheDocument()
    expect(screen.getByText('المستخدمون الجدد — آخر 30 يومًا')).toBeInTheDocument()
    expect(screen.getByText('توزيع الأدوار')).toBeInTheDocument()
    expect(screen.getByText('النشطون مقابل المقفلون')).toBeInTheDocument()
    expect(screen.getByText('البريد المؤكَّد مقابل المعلَّق')).toBeInTheDocument()
    expect(screen.getByText('20%')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('keeps every chart SVG left-to-right while its accessible name is Arabic', async () => {
    const { container } = renderPage()
    await screen.findByText('إجمالي المستخدمين')

    // Trend, role donut and the two status splits; each one is the chart's own SVG root.
    expect(container.querySelectorAll('svg[role="img"][direction="ltr"]')).toHaveLength(4)
    // jsdom cannot resolve MUI's Paper shadow variable under getByRole, so the names are read by label.
    expect(
      screen.getByLabelText('عدد المستخدمين المسجلين حديثًا يوميًا خلال آخر 30 يومًا، بإجمالي 4'),
    ).toBeInTheDocument()
    expect(screen.getByLabelText(/^توزيع الأدوار: /)).toBeInTheDocument()
  })

  it('shows Arabic empty states for charts with no data', async () => {
    server.use(
      http.get('*/api/v1/admin/dashboard/summary', () =>
        HttpResponse.json({
          ...summary,
          totalUsers: 0,
          activeUsers: 0,
          lockedOutUsers: 0,
          emailConfirmedUsers: 0,
          emailPendingUsers: 0,
          twoFactorEnabledUsers: 0,
          newUsersLast30Days: [{ date: '2026-07-28', newUsers: 0 }],
          roleDistribution: [{ roleName: 'User', userCount: 0 }],
        } satisfies DashboardSummary),
      ),
    )
    renderPage()

    await waitFor(() =>
      expect(screen.getByText('لا توجد تسجيلات جديدة في هذه الفترة.')).toBeInTheDocument(),
    )
    expect(screen.getAllByText('لا يوجد مستخدمون مسجلون بعد.').length).toBeGreaterThan(0)
  })

  it('shows a visible Arabic error with a retry when the summary fails to load', async () => {
    server.use(
      http.get('*/api/v1/admin/dashboard/summary', () =>
        HttpResponse.json({ title: 'x' }, { status: 500 }),
      ),
    )
    renderPage()

    expect(await screen.findByText('تعذّر تحميل لوحة التحكم.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'حاول مرة أخرى' })).toBeInTheDocument()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    const { container } = renderPage()
    await screen.findByText('إجمالي المستخدمين')

    expect(await axe(container)).toHaveNoViolations()
  })
})
