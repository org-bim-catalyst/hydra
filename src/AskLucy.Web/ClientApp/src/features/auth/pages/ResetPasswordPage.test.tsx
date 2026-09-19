import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { ResetPasswordPage } from './ResetPasswordPage'

const POLICY_VERSION = '2026-07-30.1'
const VALID_LINK = '/reset-password?userId=user-1&token=AABBCC'

const server = setupServer(
  http.get('*/api/v1/cookie-policy', () =>
    HttpResponse.json({ version: POLICY_VERSION, effectiveAtUtc: '2026-07-30T00:00:00Z' }),
  ),
  http.post('*/api/v1/auth/password/reset/validate', () => new HttpResponse(null, { status: 204 })),
  http.post('*/api/v1/auth/password/reset', () => new HttpResponse(null, { status: 204 })),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage(url = VALID_LINK) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[url]}>
      <QueryClientProvider client={queryClient}>
        <ResetPasswordPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

async function submit(
  user: ReturnType<typeof userEvent.setup>,
  newPassword: string,
  confirmPassword = newPassword,
) {
  // `find`, not `get`: the page holds the form back until the link has been checked on load.
  await user.type(await screen.findByLabelText('New password'), newPassword)
  await user.type(screen.getByLabelText('Confirm new password'), confirmPassword)
  await user.click(screen.getByRole('button', { name: 'Set new password' }))
}

describe('ResetPasswordPage (specs/058-password-recovery US2)', () => {
  it('confirms the change and sends the user back to sign in', async () => {
    const user = userEvent.setup()
    renderPage()

    await submit(user, 'N3w-Passw0rd!')

    expect(await screen.findByText(/Your password has been changed/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to sign in' })).toBeInTheDocument()
  })

  it('blocks submission when the two passwords differ (FR-008)', async () => {
    const user = userEvent.setup()
    renderPage()

    await submit(user, 'N3w-Passw0rd!', 'N3w-Passw0rd?')

    expect(await screen.findByText('Both passwords must match.')).toBeInTheDocument()
    expect(screen.queryByText(/Your password has been changed/)).not.toBeInTheDocument()
  })

  // The password below satisfies the client-side checklist: the point is that the server stays
  // authoritative and can still refuse a password the client was willing to send.
  it('renders each failed policy rule the server reports (FR-007)', async () => {
    server.use(
      http.post('*/api/v1/auth/password/reset', () =>
        HttpResponse.json(
          {
            title: 'Password does not meet requirements',
            status: 400,
            errors: { newPassword: ['Passwords must have at least one digit.', 'Passwords must have at least one uppercase letter.'] },
          },
          { status: 400 },
        ),
      ),
    )
    const user = userEvent.setup()
    renderPage()

    await submit(user, 'N3w-Passw0rd!')

    expect(await screen.findByText('Passwords must have at least one digit.')).toBeInTheDocument()
    expect(screen.getByText('Passwords must have at least one uppercase letter.')).toBeInTheDocument()
  })

  it('offers a fresh link when the server rejects this one', async () => {
    server.use(
      http.post('*/api/v1/auth/password/reset', () =>
        HttpResponse.json({ title: 'Reset link is no longer valid', status: 400 }, { status: 400 }),
      ),
    )
    const user = userEvent.setup()
    renderPage()

    await submit(user, 'N3w-Passw0rd!')

    expect(await screen.findByText(/no longer valid/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Request a new link' })).toBeInTheDocument()
  })

  it('rejects an already-used link on arrival instead of showing the form', async () => {
    server.use(
      http.post('*/api/v1/auth/password/reset/validate', () =>
        HttpResponse.json({ title: 'Reset link is no longer valid', status: 400 }, { status: 400 }),
      ),
    )
    renderPage()

    expect(await screen.findByText(/has expired, has already been used/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Request a new link' })).toBeInTheDocument()
    expect(screen.queryByLabelText('New password')).not.toBeInTheDocument()
  })

  it('ticks each policy rule off as the password satisfies it', async () => {
    const user = userEvent.setup()
    renderPage()

    const field = await screen.findByLabelText('New password')
    // Anchored: "— not met" also ends in "met", so an unanchored match would pass either way.
    const uppercaseRule = () => screen.getByText('An uppercase letter').closest('li')
    expect(uppercaseRule()).toHaveTextContent(/— not met$/)

    await user.type(field, 'N3w-Passw0rd!')

    expect(uppercaseRule()).toHaveTextContent(/— met$/)
    expect(screen.getByText(/Password strength: /)).toBeInTheDocument()
  })

  it('does not even show the form when the link is missing its query parameters', () => {
    renderPage('/reset-password')

    expect(screen.getByText(/missing information/)).toBeInTheDocument()
    expect(screen.queryByLabelText('New password')).not.toBeInTheDocument()
  })
})
