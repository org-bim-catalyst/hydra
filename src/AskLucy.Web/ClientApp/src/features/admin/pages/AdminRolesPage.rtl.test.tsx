import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { configure, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { PagedResult, RoleSummary } from '../api/adminRolesApi'
import { AdminRolesPage } from './AdminRolesPage'

expect.extend(toHaveNoViolations)
// The first render pulls in the whole admin shell, which is slow when the suite runs in parallel.
vi.setConfig({ testTimeout: 20000 })
configure({ asyncUtilTimeout: 8000 })

vi.mock('../../../hooks/useIsSuperUser', () => ({ useIsSuperUser: vi.fn(() => false) }))

const roles: RoleSummary[] = [
  {
    id: 'role-1',
    name: 'Project Reviewer',
    description: 'Custom role',
    isBuiltIn: false,
    isDefault: false,
    lockedPermissionKeys: [],
    permissionKeys: ['admin.users.view', 'admin.ai-providers.manage'],
    userCount: 3,
    modifiedAtUtc: '2026-09-01T00:00:00Z',
    concurrencyStamp: 'stamp-1',
  },
  {
    id: 'role-2',
    name: 'Auditor',
    description: null,
    isBuiltIn: false,
    isDefault: false,
    lockedPermissionKeys: [],
    permissionKeys: ['admin.users.view'],
    userCount: 1,
    modifiedAtUtc: null,
    concurrencyStamp: 'stamp-2',
  },
]

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions: [] }),
  ),
  http.get('*/api/v1/admin/roles', () =>
    HttpResponse.json<PagedResult<RoleSummary>>({
      items: roles,
      totalCount: roles.length,
      page: 1,
      pageSize: 20,
    }),
  ),
  http.get('*/api/v1/admin/roles/administrator/content-access', () =>
    HttpResponse.json({ granted: false }),
  ),
  http.delete('*/api/v1/admin/roles/:id', () =>
    HttpResponse.json({ title: 'تعذّر', detail: 'تعذّر حذف الدور.' }, { status: 409 }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
  vi.mocked(useIsSuperUser).mockReturnValue(false)
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
        <LocalizedSurface scope="subtree">
          <AdminRolesPage />
        </LocalizedSurface>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const isolated = (text: string) => `⁨${text}⁩`
const openActions = async (name: string) => {
  fireEvent.click(await screen.findByLabelText(`إجراءات ${isolated(name)}`))
}

describe('AdminRolesPage in Arabic (T213)', () => {
  it('renders in ar/rtl with Arabic text, plural counts and no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderPage()

    expect(await screen.findByText('Project Reviewer')).toBeInTheDocument()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('دوران')).toBeInTheDocument()
    expect(screen.getByText('صلاحيتان')).toBeInTheDocument()
    expect(screen.getByText('صلاحية واحدة')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إنشاء دور' })).toBeInTheDocument()
    expect(screen.getByLabelText('البحث بالاسم')).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'الوصف' })).toBeInTheDocument()
    expect(screen.getByText('عدد الصفوف في الصفحة:')).toBeInTheDocument()
    expect(screen.getByLabelText('تحديد كل الأدوار المخصصة في هذه الصفحة')).toBeInTheDocument()
    expect(screen.getByLabelText(`تحديد ${isolated('Project Reviewer')}`)).toBeInTheDocument()
    // A role's name is user data: shown as given, in an isolate.
    expect(screen.getByText('Project Reviewer').tagName).toBe('BDI')
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the selection toolbar in Arabic', async () => {
    renderPage()
    fireEvent.click(await screen.findByLabelText(`تحديد ${isolated('Project Reviewer')}`))

    expect(await screen.findByText('تم تحديد 1')).toBeInTheDocument()
    expect(screen.getByText('حذف المحدد')).toBeInTheDocument()
  })

  it('shows an Arabic empty state and an Arabic error with retry', async () => {
    server.use(
      http.get('*/api/v1/admin/roles', () =>
        HttpResponse.json<PagedResult<RoleSummary>>({
          items: [],
          totalCount: 0,
          page: 1,
          pageSize: 20,
        }),
      ),
    )
    const { unmount } = renderPage()
    expect(await screen.findByText('لم يتم العثور على أدوار.')).toBeInTheDocument()
    unmount()

    server.use(http.get('*/api/v1/admin/roles', () => HttpResponse.error()))
    renderPage()
    expect(await screen.findByText('تعذّر تحميل الأدوار.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument()
  })

  it('offers the Super User switch and duplicate in Arabic, with an unisolated suggested name', async () => {
    vi.mocked(useIsSuperUser).mockReturnValue(true)
    renderPage()

    expect(await screen.findByText('يحق للمشرفين عرض محتوى المستخدمين')).toBeInTheDocument()
    await openActions('Project Reviewer')
    fireEvent.click(await screen.findByText('تكرار…'))

    expect(await screen.findByText(/تكرار .*Project Reviewer/)).toBeInTheDocument()
    expect(screen.getByDisplayValue('نسخة من Project Reviewer')).toBeInTheDocument()
    expect(document.querySelector('.MuiDialog-root')).toHaveAttribute('dir', 'rtl')
    expect(
      screen.getByText(/يحفظ دورًا مخصصًا جديدًا بصلاحيتَي .*Project Reviewer/),
    ).toBeInTheDocument()
    expect(screen.getByText('إلغاء')).toBeInTheDocument()
  })

  it('opens the editor in Arabic and validates in Arabic', async () => {
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'إنشاء دور' }))

    // The dialog is open, so the page behind it is aria-hidden: query by text.
    expect(await screen.findByText('الوصف', { selector: 'label' })).toBeInTheDocument()
    expect(screen.queryByText('يجب أن يتراوح اسم الدور بين 2 و50 حرفًا.')).not.toBeInTheDocument()
    fireEvent.click(screen.getByText('إنشاء', { selector: 'button' }))

    expect(await screen.findByText('يجب أن يتراوح اسم الدور بين 2 و50 حرفًا.')).toBeInTheDocument()
    expect(screen.getByText('اختر صلاحية واحدة على الأقل.')).toBeInTheDocument()
    // The permission picker: Arabic area labels and level labels, identifier keys never shown.
    expect(screen.getAllByText('المستخدمون').length).toBeGreaterThan(0)
    expect(screen.getAllByText('مزوّدو الذكاء الاصطناعي').length).toBeGreaterThan(0)
    expect(screen.getAllByText('عرض').length).toBeGreaterThan(0)
    expect(screen.queryByText('admin.users.view')).not.toBeInTheDocument()
  })

  it('shows a role permissions dialog in Arabic and searches the Arabic text', async () => {
    renderPage()
    fireEvent.click(await screen.findByLabelText(`عرض صلاحيات ${isolated('Project Reviewer')}`))

    expect(await screen.findByText(/صلاحيات .*Project Reviewer/)).toBeInTheDocument()
    expect(screen.getByText('عرض المستخدمين')).toBeInTheDocument()
    expect(screen.getByText('عرض قائمة المستخدمين وتفاصيلهم.')).toBeInTheDocument()
    expect(screen.getByText('إدارة مزوّدي الذكاء الاصطناعي')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('البحث في الصلاحيات'), { target: { value: 'مزوّد' } })
    await waitFor(() => expect(screen.queryByText('عرض المستخدمين')).not.toBeInTheDocument())
    expect(screen.getByText('إدارة مزوّدي الذكاء الاصطناعي')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('البحث في الصلاحيات'), { target: { value: 'zzz' } })
    expect(await screen.findByText(/لا توجد صلاحيات تطابق/)).toBeInTheDocument()
  })

  it('confirms deletion in Arabic and surfaces the server error in a snackbar', async () => {
    renderPage()
    await openActions('Project Reviewer')
    fireEvent.click(await screen.findByText('حذف…'))

    expect(await screen.findByText(/حذف .*Project Reviewer.*؟/)).toBeInTheDocument()
    expect(
      screen.getByText(/يشغل 3 مستخدمين هذا الدور حاليًا وسيُنقلون إلى دور .*User/),
    ).toBeInTheDocument()
    expect(screen.getByText(/لا يمكن التراجع عن هذا الإجراء/)).toBeInTheDocument()

    const dialog = document.querySelector('.MuiDialog-root') as HTMLElement
    fireEvent.click(within(dialog).getByText('حذف', { selector: 'button' }))
    expect(await screen.findByText('تعذّر حذف الدور.')).toBeInTheDocument()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    const { container } = renderPage()
    await screen.findByText('Project Reviewer')

    expect(await axe(container)).toHaveNoViolations()
  })
})
