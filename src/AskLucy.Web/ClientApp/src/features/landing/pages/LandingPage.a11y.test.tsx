import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { LandingPage } from './LandingPage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/cookie-policy', () =>
    HttpResponse.json({ version: '2026-07-30.1', effectiveAtUtc: '2026-07-30T00:00:00Z' }),
  ),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('LandingPage accessibility (spec.md FR-016, constitution §7/§10)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container } = render(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <LandingPage />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
