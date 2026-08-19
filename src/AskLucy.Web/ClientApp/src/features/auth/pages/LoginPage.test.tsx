import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { LoginPage } from './LoginPage'

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

function renderLoginPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <LoginPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('LoginPage (spec.md FR-007/FR-009/FR-017/FR-021)', () => {
  it('renders the Flumeria-branded AuthLayout shell (FR-007/FR-010)', () => {
    renderLoginPage()

    expect(screen.getByText('Flumeria')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
  })

  it('still offers the social login providers unchanged — no regression (FR-009)', () => {
    renderLoginPage()

    expect(screen.getByRole('link', { name: 'Continue with Google' })).toHaveAttribute('href', expect.stringContaining('/auth/external/google/challenge'))
    expect(screen.getByRole('link', { name: 'Continue with Facebook' })).toHaveAttribute('href', expect.stringContaining('/auth/external/facebook/challenge'))
  })

  it('still shows a visible error for invalid credentials — no regression (FR-009/FR-017)', async () => {
    server.use(http.post('*/api/v1/auth/login', () => HttpResponse.json({ title: 'Invalid credentials' }, { status: 401 })))
    const user = userEvent.setup()
    renderLoginPage()

    await user.type(screen.getByLabelText('Email address'), 'someone@example.com')
    await user.type(screen.getByLabelText('Password'), 'wrong-password')
    await user.click(screen.getByRole('button', { name: 'Sign In' }))

    expect(await screen.findByText('Invalid email or password.')).toBeInTheDocument()
  })

  it('still shows the two-factor step when the backend requires it — no regression (FR-009)', async () => {
    server.use(
      http.post('*/api/v1/auth/login', () =>
        HttpResponse.json({ userId: 'user-1', accessToken: null, expiresAtUtc: null, refreshToken: null, requiresTwoFactor: true }),
      ),
    )
    const user = userEvent.setup()
    renderLoginPage()

    await user.type(screen.getByLabelText('Email address'), 'someone@example.com')
    await user.type(screen.getByLabelText('Password'), 'correct-password')
    await user.click(screen.getByRole('button', { name: 'Sign In' }))

    expect(await screen.findByRole('heading', { name: 'Verify your identity' })).toBeInTheDocument()
  })

  it('records a FunnelCompleted/SignIn analytics event on successful sign-in, once consent is granted (FR-021)', async () => {
    grantAnalyticsConsent()
    server.use(
      http.post('*/api/v1/auth/login', () =>
        HttpResponse.json({
          userId: 'user-1',
          accessToken: 'token',
          expiresAtUtc: '2026-08-17T00:00:00Z',
          refreshToken: 'refresh',
          requiresTwoFactor: false,
        }),
      ),
    )
    const user = userEvent.setup()
    renderLoginPage()

    await user.type(screen.getByLabelText('Email address'), 'someone@example.com')
    await user.type(screen.getByLabelText('Password'), 'correct-password')
    await user.click(screen.getByRole('button', { name: 'Sign In' }))

    await waitFor(() => expect(funnelEventsCallCount).toBeGreaterThan(0))
  })
})
