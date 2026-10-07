import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
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
import type { AdminDelivery } from '../api/adminNotificationsApi'
import { AdminNotificationDeliveriesPage } from './AdminNotificationDeliveriesPage'

expect.extend(toHaveNoViolations)

const plain = (text: string) => text.replace(/[⁦-⁩]/g, '')
const hasText = (expected: string) => (_: string, element: Element | null) =>
  element !== null && element.children.length === 0 && plain(element.textContent ?? '') === expected

const delivery = (id: string, overrides: Partial<AdminDelivery> = {}): AdminDelivery => ({
  deliveryId: id,
  notificationId: `n-${id}`,
  type: 'workflow.execution.failed',
  category: 'Workflow',
  channel: 'Email',
  status: 'DeadLettered',
  failureKind: 'RetryLimitReached',
  failureReason: 'تعذّر على خادم البريد قبول الرسالة.',
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

let items: AdminDelivery[] = []
let retryFails = false

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: ['User'],
      permissions: ['admin.notifications.view', 'admin.notifications.manage'],
    }),
  ),
  http.get('*/api/v1/admin/notifications/deliveries', () =>
    HttpResponse.json({ items, nextCursor: null }),
  ),
  http.get('*/api/v1/admin/notifications/deliveries/:id', ({ params }) => {
    const found = items.find((i) => i.deliveryId === params.id)!
    return HttpResponse.json({
      delivery: found,
      notification: {
        title: 'Workflow failed',
        createdAtUtc: '2026-10-06T09:00:00Z',
        language: 'ar',
        templateVersionId: null,
      },
    })
  }),
  http.post('*/api/v1/admin/notifications/deliveries/:id/actions/retry', ({ params }) =>
    retryFails
      ? HttpResponse.json({ title: 'Error', detail: 'خدمة البريد غير متاحة.' }, { status: 503 })
      : HttpResponse.json({ deliveryId: params.id, status: 'Pending' }, { status: 202 }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => {
  setTheme()
  items = [delivery('d1')]
})
afterEach(() => {
  server.resetHandlers()
  retryFails = false
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  seedArabic(queryClient)
  const router = createMemoryRouter(
    [
      {
        path: '/admin/notifications/deliveries',
        element: (
          <LocalizedSurface scope="page">
            <AdminNotificationDeliveriesPage />
          </LocalizedSurface>
        ),
      },
    ],
    { initialEntries: ['/admin/notifications/deliveries'] },
  )
  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

/** Buttons are found by their text: role queries trip a jsdom bug once a drawer or dialog is open. */
const button = (text: string) => screen.getByText(text).closest('button') as HTMLButtonElement

describe('AdminNotificationDeliveriesPage in Arabic (T218)', () => {
  it('renders in ar/rtl with Arabic copy; addresses and identifiers stay isolated left-to-right', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    expect(await screen.findByText('عمليات التسليم')).toBeInTheDocument()
    expect(document.documentElement).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('ما تعذّر وصوله إلى الأشخاص، ولماذا')).toBeInTheDocument()

    const address = await screen.findByText('l•••@bimcatalyst.com')
    expect(address.tagName.toLowerCase()).toBe('bdi')
    expect(address).toHaveAttribute('dir', 'ltr')
    expect(screen.getByText('workflow.execution.failed')).toHaveAttribute('dir', 'ltr')

    expect(screen.getByRole('table', { name: 'عمليات التسليم' })).toBeInTheDocument()
    expect(screen.getByText('المحاولات')).toBeInTheDocument()
    expect(screen.getByText('آخر محاولة')).toBeInTheDocument()
    // Status and channel are labelled; the failure reason is the server's own text.
    expect(screen.getByText('غير قابل للتسليم')).toBeInTheDocument()
    expect(screen.getByText('البريد الإلكتروني')).toBeInTheDocument()
    expect(screen.getByText('تعذّر على خادم البريد قبول الرسالة.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument()
    expect(screen.getByText('إعادة محاولة المحدد')).toBeInTheDocument()
    // Western digits and a Gregorian date, in Arabic month-free numeric form.
    expect(screen.getByText(/2026/)).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('says in Arabic when no delivery matches', async () => {
    items = []
    renderPage()
    expect(await screen.findByText('لا توجد عمليات تسليم تطابق هذه المرشحات.')).toBeInTheDocument()
  })

  it('opens the details drawer in Arabic, mirrored to the end edge', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()
    await screen.findByText('l•••@bimcatalyst.com')

    fireEvent.click(screen.getByRole('button', { name: /^فتح تسليم/ }))

    expect(await screen.findByText('معرّف الارتباط')).toBeInTheDocument()
    expect(screen.getByText('استجابة المزوّد')).toBeInTheDocument()
    expect(screen.getByText('corr-1')).toHaveAttribute('dir', 'ltr')
    // The panel opens on the end edge, which is the left one in a right-to-left page.
    const paper = document.querySelector('.MuiDrawer-paper') as HTMLElement
    expect(getComputedStyle(paper).left).toBe('0px')
    expect(getComputedStyle(paper).right).toBe('auto')
    expect(document.querySelector('.MuiDrawer-root')).toHaveAttribute('dir', 'rtl')
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('confirms a retry in Arabic', async () => {
    renderPage()
    await screen.findByText('l•••@bimcatalyst.com')

    fireEvent.click(button('إعادة المحاولة'))

    expect(
      await screen.findByText('أُدرجت عملية التسليم في قائمة الانتظار لإرسالها مرة أخرى.'),
    ).toBeInTheDocument()
  })

  it('shows a failed retry with the server message as returned', async () => {
    retryFails = true
    renderPage()
    await screen.findByText('l•••@bimcatalyst.com')

    fireEvent.click(button('إعادة المحاولة'))

    await waitFor(() =>
      expect(
        screen.getByText(hasText('لم تتم إعادة المحاولة. خدمة البريد غير متاحة.')),
      ).toBeInTheDocument(),
    )
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage()
      await screen.findByText('l•••@bimcatalyst.com')
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})
