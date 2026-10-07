import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { NotificationDetail, NotificationItem } from '../api/notificationsApi'
import { NotificationsPage } from './NotificationsPage'

expect.extend(toHaveNoViolations)

const item: NotificationItem = {
  id: 'notif-1',
  category: 'Document',
  type: 'document.processed',
  title: 'المستند جاهز',
  message: 'انتهت معالجة مستندك.',
  priority: 'High',
  status: 'Delivered',
  language: 'ar',
  createdAtUtc: new Date().toISOString(),
  readAtUtc: null,
  expiresAtUtc: null,
  action: null,
  relatedItem: null,
}
const detail: NotificationDetail = { ...item, metadata: { documentId: 'doc-1' } }

const server = setupServer(
  http.get('*/api/v1/notifications', () => HttpResponse.json({ items: [item], nextCursor: null })),
  http.get('*/api/v1/notifications/notif-1', () => HttpResponse.json(detail)),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => {
  setTheme()
  // jsdom has no layout, so the virtualizer needs a plausible height to render rows.
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(72)
})
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
  vi.restoreAllMocks()
})
afterAll(() => server.close())

function renderPage(entry = '/notifications') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/notifications" element={<NotificationsPage />} />
          <Route path="/notifications/:id" element={<NotificationsPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('NotificationsPage in Arabic (T191)', () => {
  it('renders in ar/rtl: Arabic title, filters and day header, rtl surface root, no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderPage()

    expect(await screen.findByText('المستند جاهز')).toBeInTheDocument()
    const root = container.querySelector('div[lang="ar"]')
    expect(root).toHaveAttribute('dir', 'rtl')
    expect(root).toContainElement(screen.getByText('المستند جاهز'))
    expect(screen.getByRole('heading', { name: 'الإشعارات' })).toBeInTheDocument()
    expect(screen.getByText('اليوم')).toBeInTheDocument()
    expect(screen.getByText('غير المقروءة')).toBeInTheDocument()
    expect(screen.getByText('عالية')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('opens the details drawer in Arabic, on the rtl side', async () => {
    renderPage('/notifications/notif-1')

    await waitFor(() => expect(screen.getAllByText('المستند جاهز').length).toBeGreaterThan(1))
    expect(screen.getByText('تحديد كمقروء').closest('[dir]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('documentId: doc-1')).toBeInTheDocument()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    const { container } = renderPage()
    await screen.findByText('المستند جاهز')

    expect(await axe(container)).toHaveNoViolations()
  })
})
