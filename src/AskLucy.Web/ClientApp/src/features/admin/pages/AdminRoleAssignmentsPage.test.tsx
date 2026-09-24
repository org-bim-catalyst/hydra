import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { PagedResult, RoleAssignment, RoleSummary } from '../api/adminRolesApi'
import { AdminRoleAssignmentsPage } from './AdminRoleAssignmentsPage'

const roles: RoleSummary[] = [
  {
    id: 'role-1',
    name: 'Project Reviewer',
    description: null,
    isBuiltIn: false,
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
afterEach(() => server.resetHandlers())
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
