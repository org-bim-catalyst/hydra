import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { LocalizationSettings } from '../api/adminLocalizationApi'
import { AdminLocalizationPage } from './AdminLocalizationPage'

expect.extend(toHaveNoViolations)

const plain = (text: string) => text.replace(/[⁦-⁩]/g, '')
const hasText = (expected: string) => (_: string, element: Element | null) =>
  element !== null && element.children.length === 0 && plain(element.textContent ?? '') === expected

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
let putResponse: (() => Response) | null = null

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: ['User'],
      permissions: ['admin.notifications.view', 'admin.notifications.manage'],
    }),
  ),
  http.get('*/api/v1/admin/localization', () => HttpResponse.json(current)),
  http.put('*/api/v1/admin/localization', async ({ request }) => {
    if (putResponse) return putResponse()
    const body = (await request.json()) as { isEnabled: boolean; supportedLanguages: string[] }
    current = settings({ ...body, rowVersion: 'AAAAAAAAB9I=' })
    return HttpResponse.json(current)
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  current = settings()
  putResponse = null
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  seedArabic(queryClient)
  const router = createMemoryRouter(
    [
      {
        path: '/admin/notifications/localization',
        element: (
          <LocalizedSurface scope="page">
            <AdminLocalizationPage />
          </LocalizedSurface>
        ),
      },
    ],
    { initialEntries: ['/admin/notifications/localization'] },
  )
  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

const toggle = () => screen.findByRole('switch', { name: 'تفعيل التوطين' })

describe('AdminLocalizationPage in Arabic (T218)', () => {
  it('renders in ar/rtl with Arabic copy; language names stay in their own script and language', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    expect(await toggle()).not.toBeChecked()
    expect(document.documentElement).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('التوطين', { selector: 'h5' })).toBeInTheDocument()
    expect(screen.getByText('اللغات المدعومة')).toBeInTheDocument()
    expect(screen.getByText(/عند إيقافه، تكون كل الإشعارات/)).toBeInTheDocument()
    expect(screen.getByText('English')).toHaveAttribute('lang', 'en')
    expect(screen.getByText('العربية')).toHaveAttribute('lang', 'ar')
    expect(screen.getByText(hasText('en · مدعومة دائمًا'))).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'حفظ' })).toBeDisabled()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('saves and confirms in Arabic', async () => {
    const user = userEvent.setup()
    renderPage()

    await user.click(await toggle())
    await user.click(screen.getByRole('checkbox', { name: /العربية/ }))
    await user.click(screen.getByRole('button', { name: 'حفظ' }))

    expect(await screen.findByText('تم حفظ إعدادات التوطين.')).toBeInTheDocument()
  })

  it('offers an Arabic reload on a conflict and shows the server message as returned', async () => {
    const user = userEvent.setup()
    putResponse = () =>
      HttpResponse.json(
        { title: 'Conflict', detail: 'تم تعديل الإعدادات.', reason: 'ConcurrencyConflict' },
        { status: 409 },
      )
    renderPage()

    await user.click(await toggle())
    await user.click(screen.getByRole('button', { name: 'حفظ' }))

    expect(await screen.findByText(/غيّر شخص آخر الإعدادات/)).toBeInTheDocument()
    expect(screen.getByText(/تم تعديل الإعدادات\./)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إعادة التحميل' })).toBeInTheDocument()
  })

  it('shows a failed load with an Arabic retry', async () => {
    server.use(
      http.get('*/api/v1/admin/localization', () =>
        HttpResponse.json({ title: 'Error', detail: 'تعذّر التحميل.' }, { status: 500 }),
      ),
    )
    renderPage()

    await waitFor(() => expect(screen.getByText('تعذّر التحميل.')).toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument()
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage()
      await toggle()
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})
