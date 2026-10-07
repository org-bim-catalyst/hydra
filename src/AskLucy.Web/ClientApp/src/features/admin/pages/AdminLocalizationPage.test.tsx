import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { LocalizationSettings } from '../api/adminLocalizationApi'
import { AdminLocalizationPage } from './AdminLocalizationPage'

const settings = (overrides: Partial<LocalizationSettings> = {}): LocalizationSettings => ({
  isEnabled: false,
  supportedLanguages: ['en'],
  availableLanguages: [
    { code: 'en', nativeName: 'English', locked: true },
    { code: 'ar', nativeName: 'العربية', locked: false },
  ],
  rowVersion: 'AAAAAAAAB9E=',
  ...overrides,
})

let current = settings()
let permissions = ['admin.notifications.view', 'admin.notifications.manage']
let putResponse: (() => Response) | null = null
const puts: { ifMatch: string | null; body: unknown }[] = []

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['User'], permissions }),
  ),
  http.get('*/api/v1/admin/localization', () => HttpResponse.json(current)),
  http.put('*/api/v1/admin/localization', async ({ request }) => {
    puts.push({ ifMatch: request.headers.get('If-Match'), body: await request.json() })
    if (putResponse) return putResponse()
    const body = puts.at(-1)!.body as { isEnabled: boolean; supportedLanguages: string[] }
    current = settings({ ...body, rowVersion: 'AAAAAAAAB9I=' })
    return HttpResponse.json(current)
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  current = settings()
  permissions = ['admin.notifications.view', 'admin.notifications.manage']
  putResponse = null
  puts.length = 0
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createMemoryRouter(
    [{ path: '/admin/notifications/localization', element: <AdminLocalizationPage /> }],
    {
      initialEntries: ['/admin/notifications/localization'],
    },
  )
  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

const toggle = () => screen.findByRole('switch', { name: 'Enable localization' })

describe('AdminLocalizationPage (specs/067 US8, FR-044a)', () => {
  it('shows the current settings with English locked on', async () => {
    renderPage()

    expect(await toggle()).not.toBeChecked()
    const english = screen.getByRole('checkbox', { name: /English/ })
    expect(english).toBeChecked()
    expect(english).toBeDisabled()
    expect(screen.getByRole('checkbox', { name: /العربية/ })).not.toBeChecked()
  })

  it('saves the change with the row version in If-Match, and always sends en', async () => {
    const user = userEvent.setup()
    renderPage()

    await user.click(await toggle())
    await user.click(screen.getByRole('checkbox', { name: /العربية/ }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(puts).toHaveLength(1))
    expect(puts[0].ifMatch).toBe('AAAAAAAAB9E=')
    expect(puts[0].body).toEqual({ isEnabled: true, supportedLanguages: ['en', 'ar'] })
    expect(await screen.findByText('Localization settings saved.')).toBeInTheDocument()
    expect(await screen.findByRole('switch', { name: 'Enable localization' })).toBeChecked()
  })

  it('keeps Save disabled until something changes', async () => {
    renderPage()
    await toggle()
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('offers a reload when the settings were changed by someone else (409), and reloads them', async () => {
    const user = userEvent.setup()
    putResponse = () =>
      HttpResponse.json(
        {
          title: 'Conflict',
          detail: 'The localization settings were modified.',
          reason: 'ConcurrencyConflict',
        },
        { status: 409 },
      )
    renderPage()

    await user.click(await toggle())
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(/changed by someone else/)).toBeInTheDocument()
    current = settings({
      isEnabled: true,
      supportedLanguages: ['en', 'ar'],
      rowVersion: 'AAAAAAAAB9M=',
    })
    await user.click(screen.getByRole('button', { name: 'Reload' }))

    await waitFor(() =>
      expect(screen.getByRole('switch', { name: 'Enable localization' })).toBeChecked(),
    )
    expect(screen.queryByText(/changed by someone else/)).not.toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: /العربية/ })).toBeChecked()
  })

  it('shows the server message for a refused change (422), without a reload offer', async () => {
    const user = userEvent.setup()
    putResponse = () =>
      HttpResponse.json(
        { title: 'Unprocessable', detail: 'English must stay supported.' },
        { status: 422 },
      )
    renderPage()

    await user.click(await toggle())
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText(/English must stay supported\./)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reload' })).not.toBeInTheDocument()
  })

  it('shows an error with a retry when the settings cannot be loaded', async () => {
    server.use(
      http.get('*/api/v1/admin/localization', () =>
        HttpResponse.json(
          { title: 'Server error', detail: 'The settings failed to load.' },
          { status: 500 },
        ),
      ),
    )
    renderPage()

    expect(await screen.findByText('The settings failed to load.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('is read-only without the manage permission', async () => {
    permissions = ['admin.notifications.view']
    renderPage()

    await toggle()
    await waitFor(() =>
      expect(screen.getByRole('switch', { name: 'Enable localization' })).toBeDisabled(),
    )
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  })
})
