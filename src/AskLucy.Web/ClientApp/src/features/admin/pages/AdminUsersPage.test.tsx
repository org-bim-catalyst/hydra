import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { PagedResult, UserAdmin } from '../api/adminApi'
import { AdminUsersPage } from './AdminUsersPage'

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
    role: 'Regular',
    createdAtUtc: '2026-07-21T00:00:00Z',
  },
]

let lastRequestUrl: URL | undefined

const server = setupServer(
  http.get('*/api/v1/users', ({ request }) => {
    lastRequestUrl = new URL(request.url)
    const result: PagedResult<UserAdmin> = { items: users, totalCount: users.length, page: 1, pageSize: 20 }
    return HttpResponse.json(result)
  }),
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
  http.get('*/api/v1/users/actions/bulk-eligible-ids', () => HttpResponse.json({ ids: ['user-1', 'user-2'] })),
  http.post('*/api/v1/users/actions/bulk-lock', () =>
    HttpResponse.json({ succeededCount: 1, skipped: [{ id: 'user-2', reason: 'AlreadyLocked' }] }),
  ),
  http.post('*/api/v1/users/actions/bulk-unlock', () => HttpResponse.json({ succeededCount: 1, skipped: [] })),
  http.post('*/api/v1/users/actions/bulk-force-2fa-reset', () => HttpResponse.json({ succeededCount: 2, skipped: [] })),
  http.delete('*/api/v1/users/actions/bulk-delete', () => HttpResponse.json({ succeededCount: 2, skipped: [] })),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminUsersPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminUsersPage', () => {
  it('renders the returned users and total count', async () => {
    renderPage()

    expect(await screen.findByText('alice@example.com')).toBeInTheDocument()
    expect(screen.getByText('bob@example.com')).toBeInTheDocument()
    expect(screen.getByText('2 registered users')).toBeInTheDocument()
  })

  it('sends the search term as a query parameter when the user types', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.change(screen.getByLabelText('Search by name or email'), { target: { value: 'jane' } })

    await waitFor(() => expect(lastRequestUrl?.searchParams.get('search')).toBe('jane'))
  })

  it('toggles sort direction when a column header is clicked', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByText('Registered'))
    await waitFor(() => expect(lastRequestUrl?.searchParams.get('sortBy')).toBe('createdAtUtc'))
    expect(lastRequestUrl?.searchParams.get('sortDescending')).toBe('false')

    fireEvent.click(screen.getByText('Registered'))
    await waitFor(() => expect(lastRequestUrl?.searchParams.get('sortDescending')).toBe('true'))
  })

  it('requests the next page when pagination is used', async () => {
    server.use(
      http.get('*/api/v1/users', ({ request }) => {
        lastRequestUrl = new URL(request.url)
        const result: PagedResult<UserAdmin> = { items: users, totalCount: 25, page: 1, pageSize: 20 }
        return HttpResponse.json(result)
      }),
    )
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByRole('button', { name: /next page/i }))

    await waitFor(() => expect(lastRequestUrl?.searchParams.get('page')).toBe('2'))
  })

  it('shows no bulk toolbar until a row is selected, then shows the persistent selected-count label', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    expect(screen.queryByText(/selected$/)).not.toBeInTheDocument()

    fireEvent.click(screen.getByLabelText('Select alice@example.com'))

    expect(await screen.findByText('1 selected')).toBeInTheDocument()
  })

  it('reads "Lock selected" by default and switches to "Unlock selected" when every selected user is locked', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByLabelText('Select alice@example.com'))
    expect(await screen.findByText('Lock selected')).toBeInTheDocument()

    fireEvent.click(screen.getByLabelText('Select bob@example.com'))
    expect(await screen.findByText('Unlock selected')).toBeInTheDocument()
  })

  it('goes indeterminate on the header checkbox when only some eligible rows are selected', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByLabelText('Select alice@example.com'))

    const headerCheckbox = screen.getByLabelText('Select all eligible users on this page') as HTMLInputElement
    expect(headerCheckbox.indeterminate).toBe(true)
  })

  it('selects every eligible row via the header "select all" checkbox', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByLabelText('Select all eligible users on this page'))

    expect(await screen.findByText('2 selected')).toBeInTheDocument()
  })

  it('disables "select all" when there are zero eligible rows on the page', async () => {
    server.use(
      http.get('*/api/v1/users', () => {
        const result: PagedResult<UserAdmin> = { items: [], totalCount: 0, page: 1, pageSize: 20 }
        return HttpResponse.json(result)
      }),
    )
    renderPage()

    await waitFor(() =>
      expect(screen.getByLabelText('Select all eligible users on this page')).toBeDisabled(),
    )
  })

  it('opens the confirmation dialog with page and all-matching counts, then shows the result summary with skip reasons', async () => {
    renderPage()
    await screen.findByText('alice@example.com')

    fireEvent.click(screen.getByLabelText('Select alice@example.com'))
    fireEvent.click(await screen.findByText('Lock selected'))

    expect(await screen.findByText('1 selected on this page')).toBeInTheDocument()
    expect(await screen.findByText('All 2 matching items')).toBeInTheDocument()

    fireEvent.click(screen.getAllByText('Lock')[0])

    expect(await screen.findByText('1 item succeeded.')).toBeInTheDocument()
    expect(screen.getByText('AlreadyLocked')).toBeInTheDocument()
  })
})
