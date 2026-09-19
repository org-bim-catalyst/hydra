import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { ForgotPasswordPage } from './ForgotPasswordPage'

const POLICY_VERSION = '2026-07-30.1'

const server = setupServer(
  http.get('*/api/v1/cookie-policy', () =>
    HttpResponse.json({ version: POLICY_VERSION, effectiveAtUtc: '2026-07-30T00:00:00Z' }),
  ),
  http.post('*/api/v1/auth/password/forgot', () => new HttpResponse(null, { status: 202 })),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <ForgotPasswordPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

async function submit(user: ReturnType<typeof userEvent.setup>, email: string) {
  await user.type(screen.getByLabelText('Email address'), email)
  await user.click(screen.getByRole('button', { name: 'Send reset link' }))
}

describe('ForgotPasswordPage (specs/058-password-recovery US1)', () => {
  it('shows a neutral confirmation that does not reveal whether the account exists (FR-003)', async () => {
    const user = userEvent.setup()
    renderPage()

    await submit(user, 'someone@example.com')

    expect(await screen.findByText(/If an account exists for someone@example.com/)).toBeInTheDocument()
  })

  it('rejects a malformed address before it reaches the API', async () => {
    const user = userEvent.setup()
    renderPage()

    await submit(user, 'not-an-email')

    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(screen.queryByText(/a password reset link is on its way/)).not.toBeInTheDocument()
  })

  it('surfaces a visible alert when the request itself fails (FR-017)', async () => {
    server.use(http.post('*/api/v1/auth/password/forgot', () => HttpResponse.error()))
    const user = userEvent.setup()
    renderPage()

    await submit(user, 'someone@example.com')

    expect(await screen.findByText(/We couldn't send that request/)).toBeInTheDocument()
  })

  it('lets the user go back and try a different address', async () => {
    const user = userEvent.setup()
    renderPage()

    await submit(user, 'someone@example.com')
    await screen.findByText(/If an account exists for someone@example.com/)

    await user.click(screen.getByRole('button', { name: 'try a different address' }))

    expect(screen.getByLabelText('Email address')).toBeInTheDocument()
  })
})
