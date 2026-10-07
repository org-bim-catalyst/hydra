import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
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
import type { PagedResult, UserAdmin } from '../api/adminApi'
import { AdminUsersPage } from './AdminUsersPage'

expect.extend(toHaveNoViolations)

// Arabic params are wrapped in Unicode isolates (the effect of <bdi>), so names include them.
const iso = (text: string) => `\u2068${text}\u2069`

const users: UserAdmin[] = [
  {
    id: 'user-1',
    email: 'alice@example.com',
    firstName: 'Alice',
    lastName: 'Anders',
    emailConfirmed: true,
    twoFactorEnabled: true,
    lockoutEnabled: true,
    isLockedOut: false,
    role: 'Administrator',
    createdAtUtc: '2026-07-20T00:00:00Z',
  },
  {
    id: 'user-2',
    email: 'bob@example.com',
    firstName: 'Bob',
    lastName: 'Baker',
    emailConfirmed: false,
    twoFactorEnabled: false,
    lockoutEnabled: true,
    isLockedOut: true,
    role: 'User',
    createdAtUtc: '2026-07-21T00:00:00Z',
  },
]

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions: [] }),
  ),
  http.get('*/api/v1/users', () =>
    HttpResponse.json<PagedResult<UserAdmin>>({
      items: users,
      totalCount: users.length,
      page: 1,
      pageSize: 20,
    }),
  ),
  http.get('*/api/v1/users/me', () =>
    HttpResponse.json({
      id: 'admin-1',
      email: 'admin@example.com',
      firstName: 'Admin',
      lastName: 'User',
      birthDate: '1990-01-01',
      twoFactorEnabled: false,
      avatarFileName: null,
    }),
  ),
  http.get('*/api/v1/users/actions/bulk-eligible-ids', () =>
    HttpResponse.json({ ids: ['user-1', 'user-2'] }),
  ),
  http.post('*/api/v1/users/actions/bulk-lock', () =>
    HttpResponse.json({ succeededCount: 1, skipped: [{ id: 'user-2', reason: 'AlreadyLocked' }] }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminUsersPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminUsersPage in Arabic (T212)', () => {
  it('renders in ar/rtl with a mirrored Arabic table and no catalog fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderPage()

    expect(await screen.findByText('alice@example.com')).toBeInTheDocument()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('إدارة المستخدمين')).toBeInTheDocument()
    expect(screen.getByText('مستخدمان مسجلان')).toBeInTheDocument()
    expect(screen.getByLabelText('البحث بالاسم أو البريد الإلكتروني')).toBeInTheDocument()
    expect(screen.getByText('تاريخ التسجيل')).toBeInTheDocument()
    expect(screen.getByText('الإجراءات')).toBeInTheDocument()
    expect(screen.getByText('مقفل')).toBeInTheDocument()
    expect(screen.getByText('معلَّق')).toBeInTheDocument()
    expect(screen.getByText('عدد الصفوف في الصفحة:')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'الانتقال إلى الصفحة التالية' })).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('isolates identifier cells left-to-right and keeps Western digits in dates', async () => {
    renderPage()
    const email = await screen.findByText('alice@example.com')

    expect(email.tagName).toBe('BDI')
    expect(email).toHaveAttribute('dir', 'ltr')
    expect(screen.getByLabelText(`تحديد ${iso('alice@example.com')}`)).toBeInTheDocument()
    const cells = [...document.querySelectorAll('tbody td')].map((td) => td.textContent ?? '')
    expect(cells.some((text) => text.includes('2026'))).toBe(true)
    expect(cells.join(' ')).not.toMatch(/[٠-٩]/)
  })

  it('shows the localized selection toolbar and the scope dialog in Arabic', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByLabelText(`تحديد ${iso('alice@example.com')}`))
    expect(await screen.findByText('تم تحديد 1')).toBeInTheDocument()
    expect(screen.getByText('قفل المحدد')).toBeInTheDocument()

    fireEvent.click(screen.getByLabelText('تحديد كل المستخدمين المؤهلين في هذه الصفحة'))
    expect(await screen.findByText('تحديد العنصرين في هذه الصفحة فقط')).toBeInTheDocument()
    expect(await screen.findByText('تحديد العنصرين المطابقين')).toBeInTheDocument()
  })

  it('runs the bulk flow through the Arabic confirm, progress and result steps', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByLabelText(`تحديد ${iso('alice@example.com')}`))
    fireEvent.click(await screen.findByText('قفل المحدد'))

    expect(await screen.findByText(/^\u2068قفل\u2069 العناصر المحددة؟$/)).toBeInTheDocument()
    expect(screen.getByText(`هل تريد تنفيذ «${iso('قفل')}» على عنصر واحد؟`)).toBeInTheDocument()

    fireEvent.click(screen.getAllByText('قفل').find((el) => el.tagName === 'BUTTON')!)
    expect(await screen.findByText('تمت معالجة عنصر واحد بنجاح.')).toBeInTheDocument()
    expect(screen.getByText('تم تخطي 1:')).toBeInTheDocument()
    expect(screen.getByText('AlreadyLocked')).toBeInTheDocument()
    expect(screen.getByText('user-2').tagName).toBe('BDI')
  })

  it('shows an Arabic error with retry when the users fail to load', async () => {
    server.use(http.get('*/api/v1/users', () => HttpResponse.json({ title: 'x' }, { status: 500 })))
    renderPage()

    expect(await screen.findByText('تعذّر تحميل المستخدمين.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'حاول مرة أخرى' })).toBeInTheDocument()
  })

  it('surfaces a failed bulk-target lookup instead of swallowing it', async () => {
    server.use(
      http.get('*/api/v1/users/actions/bulk-eligible-ids', () =>
        HttpResponse.json({ title: 'x', detail: 'تعذّر تحديد العناصر.' }, { status: 500 }),
      ),
    )
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByLabelText('تحديد كل المستخدمين المؤهلين في هذه الصفحة'))
    await waitFor(() => expect(screen.getByText('تعذّر تحميل المستخدمين.')).toBeInTheDocument())
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    const { container } = renderPage()
    await screen.findByText('alice@example.com')

    expect(await axe(container)).toHaveNoViolations()
  })
})
