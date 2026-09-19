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
  await user.type(screen.getByLabelText('New password'), newPassword)
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

    await submit(user, 'passwordpassword')

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

  it('does not even show the form when the link is missing its query parameters', () => {
    renderPage('/reset-password')

    expect(screen.getByText(/missing information/)).toBeInTheDocument()
    expect(screen.queryByLabelText('New password')).not.toBeInTheDocument()
  })
})
