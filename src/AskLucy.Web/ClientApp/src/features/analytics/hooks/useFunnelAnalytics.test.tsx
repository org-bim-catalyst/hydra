import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { ReactNode } from 'react'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { usePublicCookieConsent } from '../../consent/hooks/usePublicCookieConsent'
import { useFunnelAnalytics } from './useFunnelAnalytics'

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
  // Clear the public consent cookie between tests (data-model.md PublicConsentState).
  document.cookie = 'flumeria_public_consent=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/'
})
afterAll(() => server.close())

function wrapper({ children }: { children: ReactNode }) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
}

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

function renderAnalyticsWithConsent() {
  return renderHook(() => ({ analytics: useFunnelAnalytics(), consent: usePublicCookieConsent() }), { wrapper })
}

describe('useFunnelAnalytics (spec.md FR-021, contracts/routing-and-consent-contract.md)', () => {
  it('does not call the funnel-events endpoint when consent has not been granted', async () => {
    const { result } = renderAnalyticsWithConsent()
    await waitFor(() => expect(result.current.consent.isPending).toBe(false))
    expect(result.current.consent.data?.analytics).toBeFalsy()

    result.current.analytics.recordCtaClick('SignUp')
    await new Promise((resolve) => setTimeout(resolve, 20))

    expect(funnelEventsCallCount).toBe(0)
  })

  it('calls the funnel-events endpoint once analytics consent has been granted', async () => {
    grantAnalyticsConsent()
    const { result } = renderAnalyticsWithConsent()
    await waitFor(() => expect(result.current.consent.data?.analytics).toBe(true))

    result.current.analytics.recordCtaClick('SignUp')

    await waitFor(() => expect(funnelEventsCallCount).toBeGreaterThan(0))
  })

  it('never throws when the funnel-events call fails (fire-and-forget, no user-facing surface)', async () => {
    grantAnalyticsConsent()
    server.use(http.post('*/api/v1/analytics/funnel-events', () => HttpResponse.error()))
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => undefined)

    const { result } = renderAnalyticsWithConsent()
    await waitFor(() => expect(result.current.consent.data?.analytics).toBe(true))

    expect(() => result.current.analytics.recordFunnelCompleted('SignIn')).not.toThrow()
    await waitFor(() => expect(warnSpy).toHaveBeenCalled())

    warnSpy.mockRestore()
  })
})
