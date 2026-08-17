import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { SettingsPage } from './SettingsPage'
import { SETTINGS_TAB_INDEX } from '../settingsTabs'

const server = setupServer(
  http.get('*/api/v1/profile', () => HttpResponse.json({ email: 'lucy@example.com', firstName: 'Lucy' })),
  http.get('*/api/v1/ai/providers', () => HttpResponse.json([])),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderSettings(initialTab?: number) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const entries =
    initialTab === undefined ? ['/settings'] : [{ pathname: '/settings', state: { tab: initialTab } }]
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={entries}>
        <SettingsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('SettingsPage tabs (specs/025-chat-configuration-settings, T006)', () => {
  it('renders all 8 tabs, including Chat Configuration and Chat History', async () => {
    renderSettings()
    await screen.findByRole('heading', { name: 'Settings' })

    for (const label of [
      'Security',
      'Account',
      'AI Providers',
      'Voice',
      'Chat Configuration',
      'Chat History',
      'Data',
      'Cookies',
    ]) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument()
    }
  })

  it('defaults to the Security tab when no location.state.tab is provided', async () => {
    renderSettings()
    await screen.findByRole('heading', { name: 'Settings' })

    expect(screen.getByRole('tab', { name: 'Security' })).toHaveAttribute('aria-selected', 'true')
  })

  it('seeds the initially active tab from location.state.tab', async () => {
    renderSettings(SETTINGS_TAB_INDEX.ChatConfiguration)
    await screen.findByRole('heading', { name: 'Settings' })

    expect(screen.getByRole('tab', { name: 'Chat Configuration' })).toHaveAttribute('aria-selected', 'true')
  })

  // Regression test: discovered via manual browser verification of quickstart.md — clicking
  // Chat Configuration's own "Go to AI Providers"/"Go to Voice" links (both already on
  // `/settings`) didn't switch tabs, because `useState`'s initializer only runs on first
  // mount and SettingsPage doesn't remount for a same-pathname navigation. Fixed by
  // re-syncing off `location.key`.
  it('re-syncs the active tab when navigating to /settings again while already mounted there', async () => {
    const user = userEvent.setup()
    renderSettings(SETTINGS_TAB_INDEX.ChatConfiguration)
    await screen.findByRole('heading', { name: 'Settings' })
    expect(screen.getByRole('tab', { name: 'Chat Configuration' })).toHaveAttribute('aria-selected', 'true')

    await user.click(await screen.findByRole('button', { name: 'Go to AI Providers' }))

    expect(screen.getByRole('tab', { name: 'AI Providers' })).toHaveAttribute('aria-selected', 'true')
  })
})
