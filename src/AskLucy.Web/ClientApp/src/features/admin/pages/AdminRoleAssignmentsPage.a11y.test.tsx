import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { PagedResult, RoleAssignment, RoleSummary } from '../api/adminRolesApi'
import { AdminRoleAssignmentsPage } from './AdminRoleAssignmentsPage'

expect.extend(toHaveNoViolations)

const assignments: RoleAssignment[] = [
  {
    userId: 'user-1',
    email: 'alice@example.com',
    firstName: 'Alice',
    lastName: 'Anders',
    isLockedOut: false,
    role: null,
  },
]

const server = setupServer(
  http.get('*/api/v1/admin/roles', () =>
    HttpResponse.json<PagedResult<RoleSummary>>({ items: [], totalCount: 0, page: 1, pageSize: 100 }),
  ),
  http.get('*/api/v1/admin/role-assignments', () =>
    HttpResponse.json<PagedResult<RoleAssignment>>({ items: assignments, totalCount: 1, page: 1, pageSize: 20 }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminRoleAssignmentsPage accessibility', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminRoleAssignmentsPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('alice@example.com')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
