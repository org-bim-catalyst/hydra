import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import {
  localizationHandler,
  signInWithTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { NotificationPreferences } from '../../notifications/api/notificationPreferencesApi'
import { NotificationPreferencesTab } from './NotificationPreferencesTab'

expect.extend(toHaveNoViolations)

const preferences: NotificationPreferences = {
  categories: [
    {
      category: 'Security',
      channels: [
        { channel: 'InApp', enabled: true, locked: true },
        { channel: 'Email', enabled: true, locked: true },
      ],
      frequency: 'Immediate',
      availableFrequencies: ['Immediate'],
    },
    {
      category: 'Workflow',
      channels: [
        { channel: 'InApp', enabled: true, locked: false },
        { channel: 'Email', enabled: true, locked: false },
      ],
      frequency: 'Immediate',
      availableFrequencies: ['Immediate'],
    },
    {
      category: 'Memory',
      channels: [{ channel: 'InApp', enabled: true, locked: false }],
      frequency: 'Immediate',
      availableFrequencies: ['Immediate'],
    },
  ],
}

const server = setupServer(
  localizationHandler(),
  http.get('*/api/v1/users/me/notification-preferences', () => HttpResponse.json(preferences)),
  http.put('*/api/v1/users/me/notification-preferences', () =>
    HttpResponse.json({ title: 'تعذّر', detail: 'تعذّر حفظ التفضيلات.' }, { status: 500 }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => signInWithTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderTab() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <NotificationPreferencesTab />
    </QueryClientProvider>,
  )
}

// A name with an Arabic string interpolated: the param is wrapped in Unicode isolates (the effect of <bdi>).
const switchName = (category: string, channel: string) =>
  new RegExp(`^إشعارات \\u2068${category}\\u2069 عبر \\u2068${channel}\\u2069$`)

describe('NotificationPreferencesTab in Arabic (T191)', () => {
  it('renders in ar/rtl with Arabic labels and no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderTab()

    expect(
      await screen.findByRole('switch', { name: switchName('سير العمل', 'البريد الإلكتروني') }),
    ).toBeChecked()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('table', { name: 'تفضيلات الإشعارات' })).toBeInTheDocument()
    expect(screen.getAllByText('فوري')).toHaveLength(preferences.categories.length)
    expect(screen.getByText(/لا تُرسل إشعارات/)).toBeInTheDocument()
    expect(
      screen.getByRole('switch', { name: switchName('الأمان', 'البريد الإلكتروني') }),
    ).toBeDisabled()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows an Arabic error with the server detail when saving fails', async () => {
    renderTab()
    ;(
      await screen.findByRole('switch', { name: switchName('سير العمل', 'البريد الإلكتروني') })
    ).click()

    expect(await screen.findByText(/لم يتم حفظ تغييرك/)).toHaveTextContent('تعذّر حفظ التفضيلات.')
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    signInWithTheme(mode)
    const { container } = renderTab()
    await screen.findByRole('switch', { name: switchName('سير العمل', 'البريد الإلكتروني') })

    expect(await axe(container)).toHaveNoViolations()
  })
})
