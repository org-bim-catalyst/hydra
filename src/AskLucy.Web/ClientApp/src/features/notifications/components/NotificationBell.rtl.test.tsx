import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import {
  localizationHandler,
  signInWithTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import { NotificationBell } from './NotificationBell'

expect.extend(toHaveNoViolations)

const server = setupServer(
  localizationHandler(),
  http.get('*/api/v1/notifications/unread-count', () => HttpResponse.json({ count: 3 })),
  http.get('*/api/v1/notifications', () => HttpResponse.json({ items: [], nextCursor: null })),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => signInWithTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderBell() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <NotificationBell />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('NotificationBell in Arabic (T191)', () => {
  it('renders in ar/rtl with an Arabic accessible name and no catalog fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderBell()

    // Three unread is the Arabic "few" form.
    const bell = await screen.findByRole('button', { name: 'الإشعارات، 3 إشعارات غير مقروءة' })
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(container.querySelector('div[lang="ar"]')).toContainElement(bell)
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('uses the plain Arabic label when nothing is unread', async () => {
    server.use(
      http.get('*/api/v1/notifications/unread-count', () => HttpResponse.json({ count: 0 })),
    )
    renderBell()
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'الإشعارات' })).toBeInTheDocument(),
    )
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    signInWithTheme(mode)
    const { container } = renderBell()
    await screen.findByRole('button', { name: 'الإشعارات، 3 إشعارات غير مقروءة' })

    expect(await axe(container)).toHaveNoViolations()
  })
})
