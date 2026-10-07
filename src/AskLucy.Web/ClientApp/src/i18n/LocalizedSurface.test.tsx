import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Dialog, Menu, MenuItem, Tooltip } from '@mui/material'
import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '../store/authStore'
import { useThemeStore } from '../store/themeStore'
import { LocalizedSurface } from './LocalizedSurface'
import type { MyLocalization } from './useLocalization'
import { useTheme } from '@mui/material/styles'
import { useT } from './useT'

const english: MyLocalization = {
  localizationEnabled: false,
  supportedLanguages: [{ code: 'en', nativeName: 'English' }],
  preferredLanguage: 'ar',
  effectiveLanguage: 'en',
  direction: 'ltr',
}
const arabic: MyLocalization = {
  localizationEnabled: true,
  supportedLanguages: [
    { code: 'en', nativeName: 'English' },
    { code: 'ar', nativeName: 'العربية' },
  ],
  preferredLanguage: 'ar',
  effectiveLanguage: 'ar',
  direction: 'rtl',
}

let localization: MyLocalization = english
const server = setupServer(
  http.get('*/api/v1/users/me/localization', () => HttpResponse.json(localization)),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => {
  useAuthStore.setState({ accessToken: 'token', userId: 'u1' })
  useThemeStore.setState({ mode: 'light' })
})
afterEach(() => {
  server.resetHandlers()
  localization = english
  useAuthStore.setState({ accessToken: null, userId: null })
  document.documentElement.removeAttribute('lang')
  document.documentElement.removeAttribute('dir')
})
afterAll(() => server.close())

function Probe() {
  const t = useT('common')
  const theme = useTheme()
  return (
    <p data-testid="probe" data-direction={theme.direction} data-mode={theme.palette.mode}>
      {t('actions.retry')}
    </p>
  )
}

function renderSurface(ui: React.ReactNode) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>)
}

describe('LocalizedSurface', () => {
  it('is a pass-through when localization is disabled: no wrapper, no provider, English', async () => {
    const { container } = renderSurface(
      <LocalizedSurface scope="subtree">
        <Probe />
      </LocalizedSurface>,
    )
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(container.querySelector('[dir]')).toBeNull()
    expect(container.querySelector('[lang]')).toBeNull()
    expect(screen.getByTestId('probe')).toHaveTextContent('Retry')
    expect(screen.getByTestId('probe')).toHaveAttribute('data-direction', 'ltr')
  })

  it('is a pass-through when the effective language is English even if localization is enabled', async () => {
    localization = { ...arabic, effectiveLanguage: 'en', direction: 'ltr' }
    const { container } = renderSurface(
      <LocalizedSurface scope="subtree">
        <Probe />
      </LocalizedSurface>,
    )
    await new Promise((resolve) => setTimeout(resolve, 50))
    expect(container.querySelector('[dir]')).toBeNull()
  })

  it('does not ask the server for a signed-out caller', async () => {
    useAuthStore.setState({ accessToken: null, userId: null })
    renderSurface(
      <LocalizedSurface scope="subtree">
        <Probe />
      </LocalizedSurface>,
    )
    // onUnhandledRequest is "error" and a request would be unhandled only if made; none is registered to fail here.
    expect(screen.getByTestId('probe')).toHaveTextContent('Retry')
  })

  it('wraps a subtree in <div lang dir>, switches the theme direction and keeps the light/dark mode', async () => {
    localization = arabic
    useThemeStore.setState({ mode: 'dark' })
    const { container } = renderSurface(
      <LocalizedSurface scope="subtree">
        <Probe />
      </LocalizedSurface>,
    )
    const probe = await screen.findByText('إعادة المحاولة')
    const root = container.querySelector('div[lang="ar"]')
    expect(root).toHaveAttribute('dir', 'rtl')
    expect(root).toContainElement(probe)
    expect(probe).toHaveAttribute('data-direction', 'rtl')
    expect(probe).toHaveAttribute('data-mode', 'dark')
  })

  it('page scope sets <html lang dir> while mounted and restores it on unmount', async () => {
    localization = arabic
    document.documentElement.setAttribute('lang', 'en')
    const { unmount } = renderSurface(
      <LocalizedSurface scope="page">
        <Probe />
      </LocalizedSurface>,
    )
    await waitFor(() => expect(document.documentElement).toHaveAttribute('dir', 'rtl'))
    expect(document.documentElement).toHaveAttribute('lang', 'ar')

    unmount()
    expect(document.documentElement).toHaveAttribute('lang', 'en')
    expect(document.documentElement).not.toHaveAttribute('dir')
  })

  it('gives portaled Dialog, Menu and Tooltip content dir="rtl"', async () => {
    localization = arabic
    const { container } = renderSurface(
      <LocalizedSurface scope="subtree">
        <Dialog open>
          <p>dialog body</p>
        </Dialog>
        <Menu open anchorEl={document.body}>
          <MenuItem>menu entry</MenuItem>
        </Menu>
        <Tooltip title="tip text" open>
          <button type="button">anchor</button>
        </Tooltip>
      </LocalizedSurface>,
    )
    await waitFor(() => expect(container.querySelector('div[lang="ar"]')).not.toBeNull())
    await screen.findByText('dialog body')
    // Portaled content is outside the subtree's own <div>, so each root must carry `dir` itself.
    expect(screen.getByText('dialog body').closest('[dir]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('menu entry').closest('[dir]')).toHaveAttribute('dir', 'rtl')
    expect((await screen.findByText('tip text')).closest('[dir]')).toHaveAttribute('dir', 'rtl')
  })

  it('renders without a QueryClient-backed answer: stays English while the language is loading', () => {
    localization = arabic
    renderSurface(
      <LocalizedSurface scope="subtree">
        <Probe />
      </LocalizedSurface>,
    )
    expect(screen.getByTestId('probe')).toHaveTextContent('Retry')
  })
})
