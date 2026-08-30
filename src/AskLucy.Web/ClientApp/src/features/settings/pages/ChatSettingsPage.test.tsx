import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { ChatSettingsPage } from './ChatSettingsPage'
import { CHAT_SETTINGS_TAB_INDEX } from '../chatSettingsTabs'

const server = setupServer(
  http.get('*/api/v1/profile', () => HttpResponse.json({ email: 'lucy@example.com', firstName: 'Lucy' })),
  http.get('*/api/v1/ai/providers', () => HttpResponse.json([])),
  http.get('*/api/v1/ai/preferences', () => HttpResponse.json(null)),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage(initialTab?: number) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const entries =
    initialTab === undefined
      ? ['/chat-settings']
      : [{ pathname: '/chat-settings', state: { tab: initialTab } }]
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={entries}>
        <ChatSettingsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/**
 * Voice, Chat Configuration and Chat History were three tabs inside general Settings, sitting
 * beside password changes and cookie preferences — related to each other and to nothing around
 * them. They are one page now, reached from a single "Chat settings" item in the account menu.
 */
describe('ChatSettingsPage', () => {
  it('gathers the three conversation tabs onto one page', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Chat settings' })

    for (const label of ['Voice', 'Chat Configuration', 'Chat History']) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument()
    }
  })

  it('opens on Voice when no tab is requested', async () => {
    renderPage()
    await screen.findByRole('heading', { name: 'Chat settings' })

    expect(screen.getByRole('tab', { name: 'Voice' })).toHaveAttribute('aria-selected', 'true')
  })

  it('seeds the active tab from location.state.tab, so the account menu can deep-link', async () => {
    renderPage(CHAT_SETTINGS_TAB_INDEX.ChatHistory)
    await screen.findByRole('heading', { name: 'Chat settings' })

    expect(screen.getByRole('tab', { name: 'Chat History' })).toHaveAttribute('aria-selected', 'true')
  })
})
