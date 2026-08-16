import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { ExternalLoginCompletePage } from './ExternalLoginCompletePage'

const POLICY_VERSION = '2026-07-30.1'
let funnelEventsCallCount = 0

const server = setupServer(
  http.get('*/api/v1/cookie-policy', () => HttpResponse.json({ version: POLICY_VERSION, effectiveAtUtc: '2026-07-30T00:00:00Z' })),
  http.post('*/api/v1/analytics/funnel-events', () => {
    funnelEventsCallCount += 1
    return new HttpResponse(null, { status: 202 })
  }),
  http.post('*/api/v1/auth/external/complete', () =>
    HttpResponse.json({ userId: 'user-1', accessToken: 'token', expiresAtUtc: '2026-08-17T00:00:00Z', refreshToken: 'refresh', requiresTwoFactor: false }),
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
    JSON.stringify({ policyVersion: POLICY_VERSION, functional: true, analytics: true, marketing: false, decidedAtUtc: new Date().toISOString() }),
  )
  document.cookie = `flumeria_public_consent=${value}; path=/`
}

function renderAt(search: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[`/auth/external-complete${search}`]}>
      <QueryClientProvider client={queryClient}>
        <ExternalLoginCompletePage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('ExternalLoginCompletePage (spec.md FR-007/FR-020/FR-021)', () => {
  it('renders the Flumeria-branded AuthLayout shell while exchanging the code (FR-007/FR-010)', () => {
    renderAt('?code=one-time-code')

    expect(screen.getByText('Flumeria')).toBeInTheDocument()
  })

  it('records a FunnelCompleted/SignIn analytics event on a successful social-login round-trip (FR-021)', async () => {
    grantAnalyticsConsent()
    renderAt('?code=one-time-code')

    await waitFor(() => expect(funnelEventsCallCount).toBeGreaterThan(0))
  })

  it('has no automatically detectable a11y violations on the error state', async () => {
    expect.extend(toHaveNoViolations)
    const { container } = renderAt('?error=access_denied')
    await screen.findByText('That sign-in link is invalid or has expired. Please try again.')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
