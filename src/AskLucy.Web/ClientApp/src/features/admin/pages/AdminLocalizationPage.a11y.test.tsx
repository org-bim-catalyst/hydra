import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { AdminLocalizationPage } from './AdminLocalizationPage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: ['User'],
      permissions: ['admin.notifications.view', 'admin.notifications.manage'],
    }),
  ),
  http.get('*/api/v1/admin/localization', () =>
    HttpResponse.json({
      isEnabled: true,
      supportedLanguages: ['en', 'ar'],
      availableLanguages: [
        { code: 'en', nativeName: 'English', locked: true },
        { code: 'ar', nativeName: 'العربية', locked: false },
      ],
      rowVersion: 'AAAAAAAAB9E=',
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminLocalizationPage accessibility', () => {
  it('has no axe violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const router = createMemoryRouter(
      [{ path: '/admin/notifications/localization', element: <AdminLocalizationPage /> }],
      {
        initialEntries: ['/admin/notifications/localization'],
      },
    )
    const { container } = render(
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )
    await screen.findByRole('switch', { name: 'Enable localization' })

    expect(await axe(container)).toHaveNoViolations()
  })
})
