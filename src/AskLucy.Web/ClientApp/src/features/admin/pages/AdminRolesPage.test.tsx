import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { useIsSuperUser } from '../../../hooks/useIsSuperUser'
import type { PagedResult, RoleSummary } from '../api/adminRolesApi'
import { AdminRolesPage } from './AdminRolesPage'

vi.mock('../../../hooks/useIsSuperUser', () => ({ useIsSuperUser: vi.fn(() => false) }))

const roles: RoleSummary[] = [
  {
    id: 'super-user',
    name: 'Super User',
    description: 'Full access',
    isBuiltIn: true,
    permissionKeys: ['admin.users.view', 'admin.users.manage'],
    userCount: 1,
    modifiedAtUtc: null,
    concurrencyStamp: 'stamp-0',
  },
  {
    id: 'role-1',
    name: 'Project Reviewer',
    description: 'Custom role',
    isBuiltIn: false,
    permissionKeys: ['admin.users.view'],
    userCount: 3,
    modifiedAtUtc: '2026-09-01T00:00:00Z',
    concurrencyStamp: 'stamp-1',
  },
]

const server = setupServer(
  // AdminShell's permission-gated nav reads the session; without this the request escapes to the
  // real network (an MSW "unhandled request" warning in CI).
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions: [] }),
  ),
  http.get('*/api/v1/admin/roles', () => {
    const result: PagedResult<RoleSummary> = { items: roles, totalCount: roles.length, page: 1, pageSize: 20 }
    return HttpResponse.json(result)
  }),
  http.get('*/api/v1/admin/roles/actions/bulk-eligible-ids', () => HttpResponse.json({ ids: ['role-1'] })),
  http.post('*/api/v1/admin/roles/actions/bulk-delete', () =>
    HttpResponse.json({ succeededCount: 1, skipped: [], unassignedUserCounts: { 'role-1': 3 } }),
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
        <AdminRolesPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminRolesPage bulk delete', () => {
  it('has no checkbox for the built-in role but shows one for the custom role', async () => {
    renderPage()
    await screen.findByText('Project Reviewer')

    expect(screen.queryByLabelText('Select Super User')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Select Project Reviewer')).toBeInTheDocument()
  })

  it('shows the persistent selected-count label and "Delete selected" once a custom role is selected', async () => {
    renderPage()
    await screen.findByText('Project Reviewer')

    fireEvent.click(screen.getByLabelText('Select Project Reviewer'))

    expect(await screen.findByText('1 selected')).toBeInTheDocument()
    expect(screen.getByText('Delete selected')).toBeInTheDocument()
  })

  it('disables "select all" when the page has zero custom roles', async () => {
    server.use(
      http.get('*/api/v1/admin/roles', () => {
        const result: PagedResult<RoleSummary> = { items: [roles[0]], totalCount: 1, page: 1, pageSize: 20 }
        return HttpResponse.json(result)
      }),
    )
    renderPage()

    await waitFor(() => expect(screen.getByLabelText('Select all custom roles on this page')).toBeDisabled())
  })

  it('goes indeterminate when only some custom roles are selected', async () => {
    server.use(
      http.get('*/api/v1/admin/roles', () => {
        const result: PagedResult<RoleSummary> = {
          items: [roles[1], { ...roles[1], id: 'role-2', name: 'Second Custom Role' }],
          totalCount: 2,
          page: 1,
          pageSize: 20,
        }
        return HttpResponse.json(result)
      }),
    )
    renderPage()
    await screen.findByText('Project Reviewer')

    fireEvent.click(screen.getByLabelText('Select Project Reviewer'))

    // MUI's Checkbox deliberately does not set the native `.indeterminate` DOM property — it
    // reflects the state via `aria-checked="mixed"` instead (see its own source comment).
    const headerCheckbox = screen.getByLabelText('Select all custom roles on this page')
    expect(headerCheckbox).toHaveAttribute('aria-checked', 'mixed')
  })

  it('confirms the resolved item count and shows the result after running via bulkDeleteRoles', async () => {
    renderPage()
    await screen.findByText('Project Reviewer')

    fireEvent.click(screen.getByLabelText('Select Project Reviewer'))
    fireEvent.click(screen.getByText('Delete selected'))

    expect(await screen.findByText('Do you want to delete 1 item?')).toBeInTheDocument()

    fireEvent.click(screen.getAllByText('Delete')[0])

    expect(await screen.findByText('1 item succeeded.')).toBeInTheDocument()
  })

  it('clicking the header checkbox asks for scope before selecting', async () => {
    renderPage()
    await screen.findByText('Project Reviewer')

    fireEvent.click(screen.getByLabelText('Select all custom roles on this page'))

    expect(await screen.findByText('Select the 1 item on this page only')).toBeInTheDocument()
    expect(await screen.findByText('Select all 1 matching items')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Select'))

    expect(await screen.findByText('1 selected')).toBeInTheDocument()
  })
})

// specs/074 T081 (FR-016g) — the Super User's switch that lets Administrators view user content.
describe('AdminRolesPage — Administrators may view user content', () => {
  const SWITCH_LABEL = 'Administrators may view user content'

  it('is not offered to a non-Super-User', async () => {
    renderPage()
    await screen.findByText('Project Reviewer')

    expect(screen.queryByLabelText(SWITCH_LABEL)).not.toBeInTheDocument()
  })

  it("shows a Super User the stored state", async () => {
    vi.mocked(useIsSuperUser).mockReturnValue(true)
    server.use(http.get('*/api/v1/admin/roles/administrator/content-access', () => HttpResponse.json({ granted: true })))
    renderPage()

    await waitFor(() => expect(screen.getByLabelText(SWITCH_LABEL)).toBeChecked())
  })

  it("toasts the server's reason when the change is refused", async () => {
    vi.mocked(useIsSuperUser).mockReturnValue(true)
    server.use(
      http.get('*/api/v1/admin/roles/administrator/content-access', () => HttpResponse.json({ granted: false })),
      http.put('*/api/v1/admin/roles/administrator/content-access', () =>
        HttpResponse.json(
          { status: 403, title: 'Super User required', detail: 'Only a Super User can grant or remove View user content.' },
          { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    renderPage()
    const toggle = await screen.findByLabelText(SWITCH_LABEL)
    await waitFor(() => expect(toggle).toBeEnabled())

    fireEvent.click(toggle)

    expect(await screen.findByText('Only a Super User can grant or remove View user content.')).toBeInTheDocument()
  })

  it('offers a non-Super-User no way to delete a role that carries View user content', async () => {
    server.use(
      http.get('*/api/v1/admin/roles', () =>
        HttpResponse.json<PagedResult<RoleSummary>>({
          items: [{ ...roles[1], permissionKeys: ['admin.operational-failures.view', 'admin.operational-failures.content.view'] }],
          totalCount: 1,
          page: 1,
          pageSize: 20,
        }),
      ),
    )
    renderPage()
    await screen.findByText('Project Reviewer')

    expect(screen.queryByLabelText('Select Project Reviewer')).not.toBeInTheDocument()
    fireEvent.click(screen.getByLabelText('Actions for Project Reviewer'))
    expect(await screen.findByText('Delete (Super User only)')).toBeInTheDocument()
  })
})
