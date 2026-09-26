import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { PagedResult, RoleSummary } from '../api/adminRolesApi'
import { AdminRolesPage } from './AdminRolesPage'

expect.extend(toHaveNoViolations)

const roles: RoleSummary[] = [
  {
    id: 'role-1',
    name: 'Project Reviewer',
    description: 'Custom role',
    isBuiltIn: false,
    isDefault: false,
    lockedPermissionKeys: [],
    permissionKeys: ['admin.users.view'],
    userCount: 3,
    modifiedAtUtc: '2026-09-01T00:00:00Z',
    concurrencyStamp: 'stamp-1',
  },
]

const server = setupServer(
  http.get('*/api/v1/admin/roles', () =>
    HttpResponse.json<PagedResult<RoleSummary>>({ items: roles, totalCount: 1, page: 1, pageSize: 20 }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminRolesPage accessibility', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminRolesPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('Project Reviewer')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
