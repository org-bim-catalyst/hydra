import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { ConfirmEmailPage } from './ConfirmEmailPage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/cookie-policy', () =>
    HttpResponse.json({ version: '2026-07-30.1', effectiveAtUtc: '2026-07-30T00:00:00Z' }),
  ),
  http.post('*/api/v1/auth/confirm-email', () => new HttpResponse(null, { status: 204 })),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderAt(search: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={[`/confirm-email${search}`]}>
      <QueryClientProvider client={queryClient}>
        <ConfirmEmailPage />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('ConfirmEmailPage (spec.md FR-007/FR-019/FR-020)', () => {
  it('renders the Flumeria-branded AuthLayout shell (FR-007/FR-010)', () => {
    renderAt('?userId=user-1&token=abc')

    expect(screen.getByText('Flumeria')).toBeInTheDocument()
  })

  it('confirms the account when navigated to directly with a valid token (FR-019)', async () => {
    renderAt('?userId=user-1&token=abc')

    expect(await screen.findByText('Your email is confirmed. You can now sign in.')).toBeInTheDocument()
  })

  it('has no automatically detectable a11y violations', async () => {
    const { container } = renderAt('?userId=user-1&token=abc')
    await screen.findByText('Your email is confirmed. You can now sign in.')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
