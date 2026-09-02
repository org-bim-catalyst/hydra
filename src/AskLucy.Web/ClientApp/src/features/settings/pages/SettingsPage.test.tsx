import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
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
  it('renders only the tabs that still belong here', async () => {
    // Four tabs left this page: "AI Providers" to the admin panel (which model answers a user is
    // a platform decision, configured there as the Chat capability), and Voice / Chat
    // Configuration / Chat History to the Chat settings page, where they sit together instead of
    // beside password changes and cookie preferences.
    renderSettings()
    await screen.findByRole('heading', { name: 'Settings' })

    for (const label of ['Security', 'Account', 'Data', 'Cookies', 'Viewer']) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument()
    }

    for (const moved of ['AI Providers', 'Voice', 'Chat Configuration', 'Chat History']) {
      expect(screen.queryByRole('tab', { name: moved })).not.toBeInTheDocument()
    }
  })

  it('defaults to the Security tab when no location.state.tab is provided', async () => {
    renderSettings()
    await screen.findByRole('heading', { name: 'Settings' })

    expect(screen.getByRole('tab', { name: 'Security' })).toHaveAttribute('aria-selected', 'true')
  })

  it('seeds the initially active tab from location.state.tab', async () => {
    renderSettings(SETTINGS_TAB_INDEX.Data)
    await screen.findByRole('heading', { name: 'Settings' })

    expect(screen.getByRole('tab', { name: 'Data' })).toHaveAttribute('aria-selected', 'true')
  })

  it('keeps every remaining tab on its original index, so saved deep links still land', async () => {
    // The tabs carry explicit values rather than positional indices. Four tabs were removed from
    // the middle of this list — AI Providers to the admin panel, and Voice/Chat Configuration/
    // Chat History to Chat settings. Positional numbering would have shifted Viewer from 8 to 4
    // and silently repointed every SETTINGS_TAB_INDEX consumer.
    renderSettings(SETTINGS_TAB_INDEX.Viewer)
    await screen.findByRole('heading', { name: 'Settings' })

    expect(screen.getByRole('tab', { name: 'Viewer' })).toHaveAttribute('aria-selected', 'true')
  })
})
