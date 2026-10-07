import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import {
  localizationHandler,
  signInWithTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { NotificationItem } from '../api/notificationsApi'
import { NotificationPopover } from './NotificationPopover'

expect.extend(toHaveNoViolations)

const item: NotificationItem = {
  id: 'notif-1',
  category: 'Workflow',
  type: 'workflow.completed',
  title: 'اكتمل سير العمل',
  message: 'اكتمل تشغيل سير العمل بنجاح.',
  priority: 'Normal',
  status: 'Delivered',
  language: 'ar',
  createdAtUtc: new Date(Date.now() - 3 * 3_600_000).toISOString(),
  readAtUtc: null,
  expiresAtUtc: null,
  action: null,
  relatedItem: { type: 'workflow', id: 'w1', available: false },
}

const server = setupServer(
  localizationHandler(),
  http.get('*/api/v1/notifications', () => HttpResponse.json({ items: [item], nextCursor: null })),
  http.post('*/api/v1/notifications/actions/mark-all-read', () =>
    HttpResponse.json({ title: 'تعذّر', detail: 'تعذّر تنفيذ الطلب.' }, { status: 500 }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => signInWithTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPopover() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <NotificationPopover anchorEl={document.body} onClose={() => {}} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

// The popover is a portal, so the surface root is the popover's own root (`dir` comes from the theme defaults).
const popoverRoot = () => screen.getByText('عرض الكل').closest('[dir]')

describe('NotificationPopover in Arabic (T191)', () => {
  it('renders in ar/rtl: Arabic chrome, rtl on the portaled root, western digits and no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPopover()

    expect(await screen.findByText('اكتمل سير العمل')).toBeInTheDocument()
    expect(popoverRoot()).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('الإشعارات')).toBeInTheDocument()
    expect(screen.getByText('تحديد الكل كمقروء')).toBeInTheDocument()
    expect(screen.getByText(/لم يعد متاحًا/)).toBeInTheDocument()
    // "3 hours ago" in Arabic, with Western digits.
    expect(screen.getByText(/3/, { selector: 'span' })).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the server-localized error detail in an error toast when mark-all-read fails', async () => {
    renderPopover()
    await screen.findByText('اكتمل سير العمل')
    screen.getByText('تحديد الكل كمقروء').click()

    expect(await screen.findByText('تعذّر تنفيذ الطلب.')).toBeInTheDocument()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    signInWithTheme(mode)
    renderPopover()
    await screen.findByText('اكتمل سير العمل')

    expect(await axe(popoverRoot() as HTMLElement)).toHaveNoViolations()
  })
})
