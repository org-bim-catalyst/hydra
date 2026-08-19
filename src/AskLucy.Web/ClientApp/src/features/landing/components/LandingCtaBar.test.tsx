import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '../../../store/authStore'
import { LandingCtaBar } from './LandingCtaBar'

const server = setupServer(
  http.get('*/api/v1/cookie-policy', () =>
    HttpResponse.json({ version: '2026-07-30.1', effectiveAtUtc: '2026-07-30T00:00:00Z' }),
  ),
  http.post('*/api/v1/analytics/funnel-events', () => new HttpResponse(null, { status: 202 })),
)

beforeAll(() => server.listen())
afterEach(() => {
  server.resetHandlers()
  useAuthStore.setState({ accessToken: null, refreshToken: null, userId: null })
})
afterAll(() => server.close())

function renderCtaBar() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={['/']}>
      <QueryClientProvider client={queryClient}>
        <Routes>
          <Route path="/" element={<LandingCtaBar />} />
          <Route path="/register" element={<div>Sign-up page</div>} />
          <Route path="/chat" element={<div>Workspace</div>} />
        </Routes>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('LandingCtaBar "Start Designing" (spec.md FR-006, US3 Scenarios 2-3)', () => {
  it('routes a signed-out visitor into the sign-up flow', async () => {
    const user = userEvent.setup()
    renderCtaBar()

    await user.click(screen.getByRole('button', { name: 'Start Designing →' }))

    expect(await screen.findByText('Sign-up page')).toBeInTheDocument()
  })

  it('routes an already-authenticated visitor directly into the workspace', async () => {
    useAuthStore.setState({ accessToken: 'token-123', refreshToken: 'refresh-123', userId: 'user-1' })
    const user = userEvent.setup()
    renderCtaBar()

    await user.click(screen.getByRole('button', { name: 'Start Designing →' }))

    expect(await screen.findByText('Workspace')).toBeInTheDocument()
  })

  it('"Explore Flumeria" scrolls to the next section instead of navigating', async () => {
    const user = userEvent.setup()
    renderCtaBar()
    const scrollIntoView = vi.fn()
    const anchor = document.createElement('div')
    anchor.id = 'how-it-works-heading'
    anchor.scrollIntoView = scrollIntoView
    document.body.appendChild(anchor)

    await user.click(screen.getByRole('button', { name: 'Explore Flumeria' }))

    expect(scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'start' })
    document.body.removeChild(anchor)
  })
})
