import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { PagedResult, UserAdmin } from '../api/adminApi'
import { AdminUsersPage } from './AdminUsersPage'

expect.extend(toHaveNoViolations)

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
]

const server = setupServer(
  http.get('*/api/v1/users', () =>
    HttpResponse.json<PagedResult<UserAdmin>>({ items: users, totalCount: 1, page: 1, pageSize: 20 }),
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
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminUsersPage accessibility (SPEC-017 Phase 8)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminUsersPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('alice@example.com')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
