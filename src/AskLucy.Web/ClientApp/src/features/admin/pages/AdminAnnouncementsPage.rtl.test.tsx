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
import type { AdminAnnouncement } from '../api/adminNotificationsApi'
import { AdminAnnouncementsPage } from './AdminAnnouncementsPage'

expect.extend(toHaveNoViolations)

const plain = (text: string) => text.replace(/[⁦-⁩]/g, '')
const hasText = (expected: string) => (_: string, element: Element | null) =>
  element !== null && element.children.length === 0 && plain(element.textContent ?? '') === expected

const announcement: AdminAnnouncement = {
  id: 'a1',
  kind: 'Maintenance',
  title: 'Scheduled maintenance',
  audience: 'AllActiveUsers',
  targetRoles: [],
  isCritical: true,
  endsAtUtc: '2026-10-07T23:00:00Z',
  publishedAtUtc: '2026-10-06T10:00:00Z',
  publishedBy: 'Ada Lovelace',
  recipientCount: 1234,
  fanOutStatus: 'Completed',
  emailQueued: 3,
  emailSent: 1200,
  emailExpired: 31,
}

let items: AdminAnnouncement[] = [announcement]
const published: unknown[] = []

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: ['User'],
      permissions: ['admin.notifications.view', 'admin.notifications.manage'],
    }),
  ),
  http.get('*/api/v1/admin/notifications/announcements', () =>
    HttpResponse.json({ items, nextCursor: null }),
  ),
  http.get('*/api/v1/admin/roles', () =>
    HttpResponse.json({
      items: [
        { id: 'role-1', name: 'Engineers' },
        { id: 'role-2', name: 'Managers' },
      ],
      totalCount: 2,
      page: 1,
      pageSize: 100,
    }),
  ),
  http.post('*/api/v1/admin/notifications/announcements', async ({ request }) => {
    published.push(await request.json())
    return HttpResponse.json(
      { id: 'a2', estimatedRecipients: 1234, emailEstimatedMinutes: 21 },
      { status: 201 },
    )
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  items = [announcement]
  published.length = 0
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
        path: '/admin/notifications/announcements',
        element: (
          <LocalizedSurface scope="page">
            <AdminAnnouncementsPage />
          </LocalizedSurface>
        ),
      },
    ],
    { initialEntries: ['/admin/notifications/announcements'] },
  )
  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

/** Open MUI dialogs break role queries in jsdom, so buttons and fields are found by their text or label. */
const button = (text: string) => screen.getByText(text).closest('button') as HTMLButtonElement
const type = (label: RegExp, value: string) =>
  fireEvent.change(screen.getByLabelText(label), { target: { value } })

async function openDialog() {
  renderPage()
  await screen.findByText('Scheduled maintenance')
  fireEvent.click(screen.getByRole('button', { name: 'إعلان جديد' }))
  await screen.findByLabelText(/^العنوان/)
}

describe('AdminAnnouncementsPage in Arabic (T218)', () => {
  it('renders in ar/rtl with Arabic copy; titles and publisher names are data', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    expect(await screen.findByText('Scheduled maintenance')).toBeInTheDocument()
    expect(document.documentElement).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('الإعلانات', { selector: 'h5' })).toBeInTheDocument()
    expect(screen.getByRole('table', { name: 'الإعلانات' })).toBeInTheDocument()
    expect(screen.getByText('حرج')).toBeInTheDocument()
    expect(screen.getByText('صيانة')).toBeInTheDocument()
    expect(screen.getByText('كل المستخدمين النشطين')).toBeInTheDocument()
    expect(screen.getByText('1234')).toBeInTheDocument()
    expect(screen.getByText(hasText('1200 مُرسل · 3 في الانتظار · 31 منتهي'))).toBeInTheDocument()
    expect(screen.getByText(hasText('بواسطة Ada Lovelace'))).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('says in Arabic when there are none', async () => {
    items = []
    renderPage()
    expect(await screen.findByText('لا توجد إعلانات بعد.')).toBeInTheDocument()
  })

  it('opens the dialog in Arabic, rtl, and validates in Arabic', async () => {
    const fallbacks = watchI18nFallbacks()
    await openDialog()

    expect(document.querySelector('.MuiDialog-root')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('حرج: أرسله أيضًا عبر البريد الإلكتروني')).toBeInTheDocument()
    fireEvent.click(button('نشر'))

    expect(await screen.findByText('أدخل عنوانًا.')).toBeInTheDocument()
    expect(screen.getByText('أدخل رسالة.')).toBeInTheDocument()
    expect(published).toHaveLength(0)

    type(/^العنوان/, 'Visit https://example.com')
    type(/^الرسالة/, 'a'.repeat(2001))
    fireEvent.click(button('نشر'))

    expect(await screen.findByText('الروابط وHTML غير مسموح بها.')).toBeInTheDocument()
    expect(await screen.findByText('يجب ألا تزيد الرسالة على 2000 حرف.')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('asks twice for a critical announcement, in Arabic, then confirms the reach in Arabic', async () => {
    await openDialog()
    type(/^العنوان/, 'صيانة مجدولة')
    type(/^الرسالة/, 'سيتوقف النظام لفترة قصيرة.')
    fireEvent.click(screen.getByLabelText('حرج: أرسله أيضًا عبر البريد الإلكتروني'))
    fireEvent.click(button('نشر'))

    expect(await screen.findByText('إرسال هذا إلى الجميع؟')).toBeInTheDocument()
    expect(
      screen.getByText(
        (_, element) =>
          element?.classList.contains('MuiAlert-message') === true &&
          plain(element.textContent ?? '') ===
            'هذا إعلان حرج. يصل إلى كل مستخدم نشط داخل التطبيق وعبر البريد الإلكتروني. لا يمكن تعديله أو التراجع عنه بعد نشره.',
      ),
    ).toBeInTheDocument()
    expect(screen.getByText('وعبر البريد الإلكتروني').tagName.toLowerCase()).toBe('strong')
    expect(screen.getByText('صيانة مجدولة')).toBeInTheDocument()

    fireEvent.click(button('النشر للجميع'))

    await waitFor(() => expect(published).toHaveLength(1))
    // 1234 is the Arabic "many" form.
    expect(
      await screen.findByText(
        hasText('تم النشر. يصل إلى نحو 1234 شخصًا، ويستغرق إرسال الرسائل نحو 21 دقيقة.'),
      ),
    ).toBeInTheDocument()
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage()
      await screen.findByText('Scheduled maintenance')
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})
