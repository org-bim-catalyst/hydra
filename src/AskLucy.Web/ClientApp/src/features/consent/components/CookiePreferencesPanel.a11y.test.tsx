import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { CookiePreferencesPanel } from './CookiePreferencesPanel'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/users/me/cookie-consent', () =>
    HttpResponse.json({
      hasConsented: true,
      requiresReconsent: false,
      policyVersion: '2026-07-30.1',
      currentPolicyVersion: '2026-07-30.1',
      essential: true,
      functional: true,
      analytics: false,
      marketing: false,
      lastUpdatedAtUtc: '2026-07-30T12:00:00Z',
    }),
  ),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('CookiePreferencesPanel accessibility (constitution §7, §10)', () => {
  it('has no automatically detectable a11y violations once preferences have loaded', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <CookiePreferencesPanel />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    await findByText('Cookie preferences')
    await findByText(/Last updated:/)

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in the error/Retry state', async () => {
    server.use(http.get('*/api/v1/users/me/cookie-consent', () => new HttpResponse(null, { status: 500 })))
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByRole } = render(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <CookiePreferencesPanel />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    await findByRole('alert')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
