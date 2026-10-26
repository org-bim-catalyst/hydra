import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { useLogout } from '../features/auth/hooks/useAuth'
import { LogoutErrorToast } from './LogoutErrorToast'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function Trigger() {
  const logout = useLogout()
  return <button onClick={() => logout.mutate()}>Log out</button>
}

function renderToast() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <Trigger />
      <LogoutErrorToast />
    </QueryClientProvider>,
  )
}

describe('LogoutErrorToast', () => {
  it('reports a sign-out the server refused, instead of swallowing it', async () => {
    server.use(http.post('*/api/v1/auth/logout', () => HttpResponse.json({ title: 'boom' }, { status: 500 })))
    renderToast()

    await userEvent.setup().click(screen.getByText('Log out'))

    expect(await screen.findByText(/couldn't reach Ask Lucy/)).toBeInTheDocument()
  })

  it('stays quiet when the sign-out succeeded', async () => {
    server.use(http.post('*/api/v1/auth/logout', () => new HttpResponse(null, { status: 204 })))
    renderToast()

    await userEvent.setup().click(screen.getByText('Log out'))

    // Wait for the request to actually land before asserting on the absence of an alert.
    await screen.findByText('Log out')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
