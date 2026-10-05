import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { AdminAppearancePage } from './AdminAppearancePage'

expect.extend(toHaveNoViolations)

// WebGL does not exist in jsdom, and the preview is a canvas: it is replaced by a plain box.
vi.mock('../../chat/components/AiPresenceCard', () => ({
  PresenceCardFrame: () => <div data-testid="card" />,
}))

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: ['User'],
      permissions: ['admin.appearance.view', 'admin.appearance.manage'],
    }),
  ),
  http.get('*/api/v1/appearance/presence-sphere', () =>
    HttpResponse.json({
      dotSizeMultiplier: 1,
      cardFillPercent: 75,
      zoomEnabled: false,
      modifiedBy: null,
      modifiedAtUtc: null,
      isDefault: true,
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminAppearancePage accessibility', () => {
  it('has no automatically detectable a11y violations (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container } = render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={createMemoryRouter([{ path: '/', element: <AdminAppearancePage /> }])} />
      </QueryClientProvider>,
    )
    await screen.findByTestId('card')

    expect(await axe(container)).toHaveNoViolations()
  })
})
