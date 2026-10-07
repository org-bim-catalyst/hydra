import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import {
  arabicLocalization,
  localizationHandler,
  signInWithTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import type { MyLocalization } from '../../../i18n/useLocalization'
import { LanguageSwitch, LanguageSwitchErrorToast } from './LanguageSwitch'

expect.extend(toHaveNoViolations)

let current: MyLocalization = arabicLocalization
let putBodies: unknown[] = []
let failPut = false

const server = setupServer(
  http.get('*/api/v1/users/me/localization', () => HttpResponse.json(current)),
  http.put('*/api/v1/users/me/localization', async ({ request }) => {
    putBodies.push(await request.json())
    if (failPut)
      return HttpResponse.json(
        { title: 'Unprocessable', detail: 'Language is not supported.' },
        { status: 422 },
      )
    const { preferredLanguage } = (putBodies.at(-1) ?? {}) as { preferredLanguage: string }
    current = {
      ...current,
      preferredLanguage,
      effectiveLanguage: preferredLanguage,
      direction: preferredLanguage === 'ar' ? 'rtl' : 'ltr',
    }
    return HttpResponse.json(current)
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => signInWithTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
  current = arabicLocalization
  putBodies = []
  failPut = false
})
afterAll(() => server.close())

function renderSwitch() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <LocalizedSurface scope="subtree">
        <LanguageSwitch />
        <LanguageSwitchErrorToast />
      </LocalizedSurface>
    </QueryClientProvider>,
  )
}

describe('LanguageSwitch (T204)', () => {
  it('offers the supported languages by their native names and marks the current one', async () => {
    renderSwitch()

    expect(
      await screen.findByRole('button', { name: 'العربية', pressed: true }),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'English', pressed: false })).toBeInTheDocument()
  })

  it('renders nothing while localization is disabled', async () => {
    current = {
      ...arabicLocalization,
      localizationEnabled: false,
      supportedLanguages: [arabicLocalization.supportedLanguages[0]],
      effectiveLanguage: 'en',
      direction: 'ltr',
    }
    const { container } = renderSwitch()
    await waitFor(() => expect(container).toBeEmptyDOMElement())
  })

  it('renders nothing when only one language is supported', async () => {
    current = {
      ...arabicLocalization,
      supportedLanguages: [arabicLocalization.supportedLanguages[0]],
      effectiveLanguage: 'en',
      direction: 'ltr',
    }
    const { container } = renderSwitch()
    await waitFor(() => expect(container).toBeEmptyDOMElement())
  })

  it('renders nothing for a signed-out caller, and does not call the server', () => {
    signOutAndResetTheme()
    const { container } = renderSwitch()
    expect(container).toBeEmptyDOMElement()
  })

  it('changes the language through PUT /users/me/localization and re-renders in the new language without a reload', async () => {
    const user = userEvent.setup()
    renderSwitch()

    await user.click(await screen.findByRole('button', { name: 'English' }))

    await waitFor(() => expect(putBodies).toEqual([{ preferredLanguage: 'en' }]))
    expect(await screen.findByText('Language')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'English', pressed: true })).toBeInTheDocument()
  })

  it('shows an error toast with the server detail and a retry when the change fails', async () => {
    const user = userEvent.setup()
    failPut = true
    renderSwitch()

    await user.click(await screen.findByRole('button', { name: 'English' }))
    expect(await screen.findByText(/Language is not supported\./)).toBeInTheDocument()

    failPut = false
    await user.click(screen.getByRole('button', { name: 'إعادة المحاولة' }))
    await waitFor(() => expect(putBodies).toHaveLength(2))
    await waitFor(() =>
      expect(screen.queryByText(/Language is not supported\./)).not.toBeInTheDocument(),
    )
  })
})

describe('LanguageSwitch in Arabic (T191)', () => {
  it('renders in ar/rtl with an Arabic label, rtl on the surface root and no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderSwitch()

    expect(await screen.findByText('اللغة')).toBeInTheDocument()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('button', { name: 'العربية' })).toHaveAttribute('lang', 'ar')
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the failure in Arabic', async () => {
    const user = userEvent.setup()
    failPut = true
    renderSwitch()

    await user.click(await screen.findByRole('button', { name: 'English' }))
    expect(await screen.findByText(/تعذّر تغيير اللغة/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    signInWithTheme(mode)
    server.use(localizationHandler())
    const { container } = renderSwitch()
    await screen.findByText('اللغة')

    expect(await axe(container)).toHaveNoViolations()
  })
})
