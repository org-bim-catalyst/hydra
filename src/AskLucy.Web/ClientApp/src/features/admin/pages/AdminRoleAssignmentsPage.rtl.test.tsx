import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { configure, fireEvent, render, screen } from '@testing-library/react'
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
import type { PagedResult, RoleAssignment, RoleSummary } from '../api/adminRolesApi'
import { AdminRoleAssignmentsPage } from './AdminRoleAssignmentsPage'

expect.extend(toHaveNoViolations)
// The first render pulls in the whole admin shell, which is slow when the suite runs in parallel.
vi.setConfig({ testTimeout: 20000 })
configure({ asyncUtilTimeout: 8000 })

vi.mock('../../../hooks/useIsSuperUser', () => ({ useIsSuperUser: vi.fn(() => false) }))

const role = (overrides: Partial<RoleSummary>): RoleSummary => ({
  id: 'role-1',
  name: 'Project Reviewer',
  description: null,
  isBuiltIn: false,
  isDefault: false,
  lockedPermissionKeys: [],
  permissionKeys: [],
  userCount: 2,
  modifiedAtUtc: null,
  concurrencyStamp: 'stamp-1',
  ...overrides,
})

const roles: RoleSummary[] = [
  role({}),
  role({ id: 'role-2', name: 'Administrator', isBuiltIn: true }),
  role({
    id: 'role-3',
    name: 'Content Auditor',
    permissionKeys: ['admin.operational-failures.content.view'],
  }),
]

const assignments: RoleAssignment[] = [
  {
    userId: 'user-1',
    email: 'alice@example.com',
    firstName: 'Alice',
    lastName: 'Anders',
    isLockedOut: false,
    role: { id: 'role-1', name: 'Project Reviewer', isBuiltIn: false },
  },
  {
    userId: 'user-2',
    email: 'bob@example.com',
    firstName: 'Bob',
    lastName: 'Baker',
    isLockedOut: true,
    role: null,
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
      pageSize: 100,
    }),
  ),
  http.get('*/api/v1/admin/role-assignments', () =>
    HttpResponse.json<PagedResult<RoleAssignment>>({
      items: assignments,
      totalCount: assignments.length,
      page: 1,
      pageSize: 20,
    }),
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
          <AdminRoleAssignmentsPage />
        </LocalizedSurface>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const isolated = (text: string) => `⁨${text}⁩`

describe('AdminRoleAssignmentsPage in Arabic (T213)', () => {
  it('renders in ar/rtl with Arabic text, ltr identifiers and no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderPage()

    expect(await screen.findByText('alice@example.com')).toBeInTheDocument()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('alice@example.com').tagName).toBe('BDI')
    expect(screen.getByText('alice@example.com')).toHaveAttribute('dir', 'ltr')
    expect(screen.getByText('مستخدمان')).toBeInTheDocument()
    expect(screen.getByLabelText('البحث بالاسم أو البريد الإلكتروني')).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'البريد الإلكتروني' })).toBeInTheDocument()
    expect(screen.getByText('مقفل')).toBeInTheDocument()
    expect(screen.getByText('نشط')).toBeInTheDocument()
    // The account with no role row shows the translated default role; the other shows its own (user data) name.
    expect(screen.getByText('مستخدم')).toBeInTheDocument()
    expect(screen.getByText('Project Reviewer')).toBeInTheDocument()
    expect(screen.getAllByText('تغيير الدور…')).toHaveLength(2)
    expect(screen.getByLabelText(`تحديد ${isolated('alice@example.com')}`)).toBeInTheDocument()
    expect(screen.getByLabelText('تحديد كل المستخدمين المؤهلين في هذه الصفحة')).toBeInTheDocument()
    expect(screen.getByText('عدد الصفوف في الصفحة:')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows an Arabic empty state', async () => {
    server.use(
      http.get('*/api/v1/admin/role-assignments', () =>
        HttpResponse.json<PagedResult<RoleAssignment>>({
          items: [],
          totalCount: 0,
          page: 1,
          pageSize: 20,
        }),
      ),
    )
    renderPage()

    expect(await screen.findByText('لم يتم العثور على تعيينات أدوار.')).toBeInTheDocument()
  })

  it('shows an Arabic error with a retry affordance when loading fails', async () => {
    server.use(http.get('*/api/v1/admin/role-assignments', () => HttpResponse.error()))
    renderPage()

    expect(await screen.findByText('تعذّر تحميل تعيينات الأدوار.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument()
  })

  it('opens the change-role dialog in Arabic, with locked roles marked', async () => {
    renderPage()
    await screen.findByText('alice@example.com')
    fireEvent.click(screen.getAllByText('تغيير الدور…')[0])

    expect(await screen.findByText(/تغيير دور .*alice@example\.com/)).toBeInTheDocument()
    expect(screen.getByText(/تعيين دور جديد يستبدل الدور الحالي/)).toBeInTheDocument()
    expect(document.querySelector('.MuiDialog-root')).toHaveAttribute('dir', 'rtl')

    // The role picker (a MUI select portaled into a popover that also carries dir).
    // (getByRole breaks under an open dialog in jsdom, so the select is found by its role attribute.)
    fireEvent.mouseDown(document.querySelector('.MuiDialog-root [role="combobox"]')!)
    expect(await screen.findByText(/Administrator.* \(مضمّن\)/)).toBeInTheDocument()
    expect(screen.getByText(/Content Auditor.* \(للمستخدم الخارق فقط\)/)).toBeInTheDocument()
    expect(document.querySelector('[role="listbox"]')).not.toBeNull()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    const { container } = renderPage()
    await screen.findByText('alice@example.com')

    expect(await axe(container)).toHaveNoViolations()
  })
})
