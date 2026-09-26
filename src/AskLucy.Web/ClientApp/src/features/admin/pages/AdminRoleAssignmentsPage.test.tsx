import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import type { PagedResult, RoleAssignment, RoleSummary } from '../api/adminRolesApi'
import { AdminRoleAssignmentsPage } from './AdminRoleAssignmentsPage'

vi.mock('../../../hooks/useIsSuperUser', () => ({ useIsSuperUser: vi.fn(() => false) }))

const roles: RoleSummary[] = [
  {
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
  },
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
  // AdminShell's permission-gated nav reads the session; without this the request escapes to the
  // real network (an MSW "unhandled request" warning in CI).
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions: [] }),
  ),
  http.get('*/api/v1/admin/roles', () =>
    HttpResponse.json<PagedResult<RoleSummary>>({ items: roles, totalCount: roles.length, page: 1, pageSize: 100 }),
  ),
  http.get('*/api/v1/admin/role-assignments', () =>
    HttpResponse.json<PagedResult<RoleAssignment>>({ items: assignments, totalCount: assignments.length, page: 1, pageSize: 20 }),
  ),
  http.get('*/api/v1/admin/role-assignments/actions/bulk-eligible-ids', () => HttpResponse.json({ ids: ['user-1'] })),
  http.post('*/api/v1/admin/role-assignments/actions/bulk-assign', () =>
    HttpResponse.json({ succeededCount: 1, skipped: [] }),
  ),
)

beforeAll(() => server.listen())
afterEach(() => {
  server.resetHandlers()
  vi.mocked(useIsSuperUser).mockReturnValue(false)
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminRoleAssignmentsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminRoleAssignmentsPage bulk assign', () => {
  it('has no checkbox for the locked user but shows one for the eligible user', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    expect(screen.getByLabelText('Select alice@example.com')).toBeInTheDocument()
    expect(screen.queryByLabelText('Select bob@example.com')).not.toBeInTheDocument()
  })

  it('shows the persistent selected-count label once a row is selected', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByLabelText('Select alice@example.com'))

    expect(await screen.findByText('1 selected')).toBeInTheDocument()
  })

  it('disables "select all" when zero rows on the page are eligible', async () => {
    server.use(
      http.get('*/api/v1/admin/role-assignments', () =>
        HttpResponse.json<PagedResult<RoleAssignment>>({ items: [assignments[1]], totalCount: 1, page: 1, pageSize: 20 }),
      ),
    )
    renderPage()

    await waitFor(() => expect(screen.getByLabelText('Select all eligible users on this page')).toBeDisabled())
  })

  it('goes indeterminate when only some eligible rows are selected', async () => {
    server.use(
      http.get('*/api/v1/admin/role-assignments', () =>
        HttpResponse.json<PagedResult<RoleAssignment>>({
          items: [assignments[0], { ...assignments[0], userId: 'user-3', email: 'carol@example.com' }],
          totalCount: 2,
          page: 1,
          pageSize: 20,
        }),
      ),
    )
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByLabelText('Select alice@example.com'))

    // MUI's Checkbox deliberately does not set the native `.indeterminate` DOM property — it
    // reflects the state via `aria-checked="mixed"` instead (see its own source comment).
    const headerCheckbox = screen.getByLabelText('Select all eligible users on this page')
    expect(headerCheckbox).toHaveAttribute('aria-checked', 'mixed')
  })

  it('confirms the resolved item count once a role is picked, then shows the result', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.mouseDown(screen.getByLabelText('Role'))
    await waitFor(() => expect(document.querySelector('ul[role="listbox"]')).not.toBeNull())
    fireEvent.click(within(document.querySelector('ul[role="listbox"]') as HTMLElement).getByText('Project Reviewer'))

    fireEvent.click(screen.getByLabelText('Select alice@example.com'))
    fireEvent.click(screen.getByText('Assign selected'))

    expect(await screen.findByText('Do you want to assign 1 item?')).toBeInTheDocument()

    fireEvent.click(screen.getAllByText('Assign')[0])

    expect(await screen.findByText('1 item succeeded.')).toBeInTheDocument()
  })

  it('clicking the header checkbox asks for scope before selecting', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByLabelText('Select all eligible users on this page'))

    expect(await screen.findByText('Select the 1 item on this page only')).toBeInTheDocument()
    expect(await screen.findByText('Select all 1 matching items')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Select'))

    expect(await screen.findByText('1 selected')).toBeInTheDocument()
  })
})

// specs/074 T081 (FR-016j) — only a Super User may give or take away a role carrying View user content.
describe('AdminRoleAssignmentsPage — roles carrying View user content', () => {
  const contentRole: RoleSummary = {
    ...roles[0],
    id: 'role-content',
    name: 'Content Reviewer',
    permissionKeys: ['admin.operational-failures.view', 'admin.operational-failures.content.view'],
  }
  const holder: RoleAssignment = {
    userId: 'user-3',
    email: 'carol@example.com',
    firstName: 'Carol',
    lastName: 'Clark',
    isLockedOut: false,
    role: { id: contentRole.id, name: contentRole.name, isBuiltIn: false },
  }

  function serveContentRole() {
    server.use(
      http.get('*/api/v1/admin/roles', () =>
        HttpResponse.json<PagedResult<RoleSummary>>({ items: [...roles, contentRole], totalCount: roles.length + 1, page: 1, pageSize: 100 }),
      ),
      http.get('*/api/v1/admin/role-assignments', () =>
        HttpResponse.json<PagedResult<RoleAssignment>>({ items: [...assignments, holder], totalCount: assignments.length + 1, page: 1, pageSize: 20 }),
      ),
    )
  }

  function openRolePicker() {
    // getByRole throws once a dialog portal is open in jsdom, so reach the Select directly.
    fireEvent.mouseDown(document.querySelector('[role="dialog"] [role="combobox"]')!)
  }

  it("gives a non-Super-User no bulk checkbox for a user holding one", async () => {
    serveContentRole()
    renderPage()
    await screen.findByText('carol@example.com')

    expect(screen.queryByLabelText('Select carol@example.com')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Select alice@example.com')).toBeInTheDocument()
  })

  it('disables such a role in the picker for a non-Super-User', async () => {
    serveContentRole()
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getAllByText('Change role…')[0])
    await screen.findByText('Change role for alice@example.com')
    await waitFor(() => {
      openRolePicker()
      expect(screen.getByText('Content Reviewer (Super User only)')).toHaveAttribute('aria-disabled', 'true')
    })
  })

  it("won't let a non-Super-User change a holder's role", async () => {
    serveContentRole()
    renderPage()
    await screen.findByText('carol@example.com')

    fireEvent.click(screen.getAllByText('Change role…')[2])

    expect(await screen.findByText("This user's role includes View user content. Only a Super User can change it.")).toBeInTheDocument()
    expect(screen.getByText('Save').closest('button')).toBeDisabled()
  })

  it('leaves such a role selectable for a Super User', async () => {
    vi.mocked(useIsSuperUser).mockReturnValue(true)
    serveContentRole()
    renderPage()
    await screen.findByText('carol@example.com')

    expect(screen.getByLabelText('Select carol@example.com')).toBeInTheDocument()
  })
})

// Every account holds a role — there is no "No role" to pick, and a User-role holder is as
// assignable as anyone on a custom role.
describe('AdminRoleAssignmentsPage — the User role', () => {
  const userRole: RoleSummary = {
    ...roles[0],
    id: 'user-role',
    name: 'User',
    isBuiltIn: true,
    isDefault: true,
  }
  const userHolder: RoleAssignment = {
    userId: 'user-4',
    email: 'dan@example.com',
    firstName: 'Dan',
    lastName: 'Dunn',
    isLockedOut: false,
    role: { id: userRole.id, name: userRole.name, isBuiltIn: true },
  }
  const legacyRoleless: RoleAssignment = { ...userHolder, userId: 'user-5', email: 'eve@example.com', role: null }

  function serveUserRole() {
    server.use(
      http.get('*/api/v1/admin/roles', () =>
        HttpResponse.json<PagedResult<RoleSummary>>({ items: [...roles, userRole], totalCount: roles.length + 1, page: 1, pageSize: 100 }),
      ),
      http.get('*/api/v1/admin/role-assignments', () =>
        HttpResponse.json<PagedResult<RoleAssignment>>({ items: [userHolder, legacyRoleless], totalCount: 2, page: 1, pageSize: 20 }),
      ),
    )
  }

  it('shows an account with no role row as holding the User role, and lets either be bulk-selected', async () => {
    serveUserRole()
    renderPage()
    await screen.findByText('eve@example.com')

    expect(screen.getAllByText('User', { selector: '.MuiChip-label' })).toHaveLength(2)
    expect(screen.getByLabelText('Select dan@example.com')).toBeInTheDocument()
    expect(screen.getByLabelText('Select eve@example.com')).toBeInTheDocument()
  })

  it('offers no "No role" choice and starts a roleless account on the User role', async () => {
    serveUserRole()
    renderPage()
    await screen.findByText('eve@example.com')

    fireEvent.click(screen.getAllByText('Change role…')[1])
    await screen.findByText('Change role for eve@example.com')
    await waitFor(() => expect(document.querySelector('[role="dialog"] [role="combobox"]')).toHaveTextContent('User'))

    fireEvent.mouseDown(document.querySelector('[role="dialog"] [role="combobox"]')!)
    // getByRole throws once a dialog portal is open in jsdom, so read the listbox directly.
    await waitFor(() => expect(document.querySelector('[role="listbox"]')).toHaveTextContent('Project Reviewer'))
    expect(document.querySelector('[role="listbox"]')).not.toHaveTextContent('No role')
  })
})
