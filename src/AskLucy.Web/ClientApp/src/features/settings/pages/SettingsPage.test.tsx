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
  it('renders every user-facing tab, no longer including AI Providers', async () => {
    // "AI Providers" was the per-user default provider/model. It moved to the admin panel — which
    // model answers a user is a platform decision, configured there as the Chat capability.
    renderSettings()
    await screen.findByRole('heading', { name: 'Settings' })

    for (const label of [
      'Security',
      'Account',
      'Voice',
      'Chat Configuration',
      'Chat History',
      'Data',
      'Cookies',
      'Viewer',
    ]) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument()
    }

    expect(screen.queryByRole('tab', { name: 'AI Providers' })).not.toBeInTheDocument()
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
  // Chat Configuration's own "Go to Voice" link (already on `/settings`) didn't switch tabs,
  // because `useState`'s initializer only runs on first mount and SettingsPage doesn't remount
  // for a same-pathname navigation. Fixed by re-syncing off `location.key`. Retargeted from the
  // removed "Go to AI Providers" link, since the behaviour under test is the re-sync, not which
  // link triggers it.
  it('re-syncs the active tab when navigating to /settings again while already mounted there', async () => {
    const user = userEvent.setup()
    renderSettings(SETTINGS_TAB_INDEX.ChatConfiguration)
    await screen.findByRole('heading', { name: 'Settings' })
    expect(screen.getByRole('tab', { name: 'Chat Configuration' })).toHaveAttribute('aria-selected', 'true')

    await user.click(await screen.findByRole('button', { name: 'Go to Voice' }))

    expect(screen.getByRole('tab', { name: 'Voice' })).toHaveAttribute('aria-selected', 'true')
  })

  it('keeps every remaining tab on its original index, so saved deep links still land', async () => {
    // The tabs carry explicit values rather than positional indices. Removing "AI Providers"
    // from the middle would otherwise shift everything after it by one and silently repoint
    // every SETTINGS_TAB_INDEX consumer at the wrong tab.
    renderSettings(SETTINGS_TAB_INDEX.Viewer)
    await screen.findByRole('heading', { name: 'Settings' })

    expect(screen.getByRole('tab', { name: 'Viewer' })).toHaveAttribute('aria-selected', 'true')
  })
})
