import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { arabicLocalization, signInWithTheme, signOutAndResetTheme } from '../i18n/testUtils'
import type { MyLocalization } from '../i18n/useLocalization'
import { UserMenu } from './UserMenu'

let localization: MyLocalization = arabicLocalization

const server = setupServer(
  http.get('*/api/v1/users/me', () =>
    HttpResponse.json({ email: 'lucy@example.com', firstName: 'Lucy', lastName: 'Ali' }),
  ),
  http.get('*/api/v1/users/me/localization', () => HttpResponse.json(localization)),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
beforeEach(() => signInWithTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
  localization = arabicLocalization
})
afterAll(() => server.close())

async function openMenu() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <UserMenu />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  await userEvent.setup().click(screen.getByRole('button', { name: 'Account menu' }))
  await screen.findByText('Application Settings')
}

describe('UserMenu language switch (specs/067 FR-044b)', () => {
  it('shows the switch when localization is enabled with more than one language', async () => {
    await openMenu()
    expect(await screen.findByText('Language')).toBeInTheDocument()
    // getByRole throws inside an open MUI menu in jsdom, so look the option up by text.
    expect(screen.getByText('العربية')).toBeInTheDocument()
  })

  it('hides the switch while localization is disabled', async () => {
    localization = {
      ...arabicLocalization,
      localizationEnabled: false,
      supportedLanguages: [arabicLocalization.supportedLanguages[0]],
      effectiveLanguage: 'en',
      direction: 'ltr',
    }
    await openMenu()
    await waitFor(() => expect(screen.queryByText('Language')).not.toBeInTheDocument())
  })

  it('hides the switch when only English is supported', async () => {
    localization = {
      ...arabicLocalization,
      supportedLanguages: [arabicLocalization.supportedLanguages[0]],
    }
    await openMenu()
    expect(screen.queryByText('Language')).not.toBeInTheDocument()
  })
})
