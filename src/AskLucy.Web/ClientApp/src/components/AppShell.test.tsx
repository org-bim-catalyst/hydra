import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '../store/authStore'
import { AppShell } from './AppShell'

const server = setupServer(
  http.get('*/api/v1/users/me', () => HttpResponse.json({ email: 'lucy@example.com', firstName: 'Lucy' })),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

beforeEach(() => {
  useAuthStore.setState({ accessToken: 'token-123', userId: 'user-1' })
})

function LocationProbe() {
  const location = useLocation()
  return <div data-testid="location">{location.pathname}</div>
}

function renderShell() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/studio']}>
        <Routes>
          <Route
            path="*"
            element={
              <AppShell title="Studio">
                <div>Page content</div>
                <LocationProbe />
              </AppShell>
            }
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AppShell account-settings shortcut', () => {
  it('opens Account settings directly from a gear icon beside the user menu', async () => {
    const user = userEvent.setup()
    renderShell()

    await user.click(screen.getByRole('button', { name: 'Account settings' }))

    expect(screen.getByTestId('location').textContent).toBe('/settings')
  })
})
