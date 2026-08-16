import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { RegisterPage } from './RegisterPage'

const POLICY_VERSION = '2026-07-30.1'
let funnelEventsCallCount = 0

const server = setupServer(
  http.get('*/api/v1/cookie-policy', () =>
    HttpResponse.json({ version: POLICY_VERSION, effectiveAtUtc: '2026-07-30T00:00:00Z' }),
  ),
  http.post('*/api/v1/analytics/funnel-events', () => {
    funnelEventsCallCount += 1
    return new HttpResponse(null, { status: 202 })
  }),
  http.post('*/api/v1/auth/register', () =>
    HttpResponse.json({ userId: 'user-1', accessToken: null, expiresAtUtc: null, refreshToken: null, requiresTwoFactor: false }),
  ),
)

beforeAll(() => server.listen())
afterEach(() => {
  server.resetHandlers()
  funnelEventsCallCount = 0
  document.cookie = 'flumeria_public_consent=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/'
})
afterAll(() => server.close())

function grantAnalyticsConsent() {
  const value = encodeURIComponent(
    JSON.stringify({
      policyVersion: POLICY_VERSION,
      functional: true,
      analytics: true,
      marketing: false,
      decidedAtUtc: new Date().toISOString(),
    }),
  )
  document.cookie = `flumeria_public_consent=${value}; path=/`
}

function renderRegisterPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <RegisterPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

async function submitValidForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Email address'), 'new-visitor@example.com')
  await user.type(screen.getByLabelText('Password'), 'a-strong-password')
  await user.type(screen.getByLabelText('Confirm password'), 'a-strong-password')
  await user.click(screen.getByRole('button', { name: 'Create Account' }))
}

describe('RegisterPage (spec.md FR-008/FR-017/FR-021, Clarifications)', () => {
  it('shows the branded confirmation-pending state on success — no redirect, no session (FR-008)', async () => {
    const user = userEvent.setup()
    renderRegisterPage()

    await submitValidForm(user)

    expect(await screen.findByText('Check your email to confirm your account.')).toBeInTheDocument()
  })

  it('records a FunnelCompleted/SignUp analytics event once consent is granted (FR-021)', async () => {
    grantAnalyticsConsent()
    const user = userEvent.setup()
    renderRegisterPage()

    await submitValidForm(user)
    await screen.findByText('Check your email to confirm your account.')

    await waitFor(() => expect(funnelEventsCallCount).toBeGreaterThan(0))
  })

  it('surfaces a visible error when the network/server fails mid-submission (FR-017, edge case)', async () => {
    server.use(http.post('*/api/v1/auth/register', () => HttpResponse.error()))
    const user = userEvent.setup()
    renderRegisterPage()

    await submitValidForm(user)

    expect(await screen.findByText('Registration failed. Please try again.')).toBeInTheDocument()
  })
})
