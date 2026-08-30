import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { UserMenu } from './UserMenu'
import { useAccountModalStore } from '../store/accountModalStore'

const server = setupServer(
  // The profile endpoint is /users/me — the old '*/api/v1/profile' handler never matched, which
  // went unnoticed while no test asserted on profile data.
  http.get('*/api/v1/users/me', () =>
    HttpResponse.json({ email: 'lucy@example.com', firstName: 'Lucy', lastName: 'Ali' }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function LocationProbe() {
  const location = useLocation()
  return <div data-testid="location">{`${location.pathname}${JSON.stringify(location.state)}`}</div>
}

function renderMenu() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/studio']}>
        <Routes>
          <Route
            path="*"
            element={
              <>
                <UserMenu />
                <LocationProbe />
              </>
            }
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

beforeEach(() => useAccountModalStore.setState({ openPath: null }))

describe('UserMenu (specs/025-chat-configuration-settings FR-011)', () => {
  it('offers one Chat settings destination in place of the two tab deep links', async () => {
    // Voice, Chat Configuration and Chat History moved onto a page of their own, so the menu
    // names the page rather than pointing at two tabs inside general Settings.
    const user = userEvent.setup()
    renderMenu()

    await user.click(screen.getByRole('button', { name: 'Account menu' }))
    await user.click(await screen.findByText('Chat settings'))

    expect(useAccountModalStore.getState().openPath).toBe('/chat-settings')
  })

  it('no longer lists the separate Chat Configuration and Chat History items', async () => {
    const user = userEvent.setup()
    renderMenu()

    await user.click(screen.getByRole('button', { name: 'Account menu' }))
    await screen.findByText('Chat settings')

    expect(screen.queryByText('Chat Configuration')).not.toBeInTheDocument()
    expect(screen.queryByText('Chat History')).not.toBeInTheDocument()
  })

  it('opens a destination over the current page instead of navigating away from it', async () => {
    // The readdy.ai reference: account destinations float above whatever you were looking at.
    // The route is untouched, so a deep link or bookmark to the same page still works.
    const user = userEvent.setup()
    renderMenu()

    await user.click(screen.getByRole('button', { name: 'Account menu' }))
    await user.click(await screen.findByText('Settings'))

    expect(useAccountModalStore.getState().openPath).toBe('/settings')
    expect(screen.getByTestId('location').textContent).toBe('/studionull')
  })

  it('shows who is signed in, the identity header the Studio card had and this one did not', async () => {
    const user = userEvent.setup()
    renderMenu()

    await user.click(screen.getByRole('button', { name: 'Account menu' }))

    expect(await screen.findByText('Lucy Ali')).toBeInTheDocument()
    expect(screen.getByText('lucy@example.com')).toBeInTheDocument()
  })
})
