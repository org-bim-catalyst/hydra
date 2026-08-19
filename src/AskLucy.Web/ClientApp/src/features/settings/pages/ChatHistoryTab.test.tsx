import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ConversationSummary, PagedResult } from '../../chat/api/chatsApi'
import { useActiveConversationStore } from '../../chat/activeConversationStore'
import { ChatHistoryTab } from './ChatHistoryTab'

function makeChat(overrides: Partial<ConversationSummary>): ConversationSummary {
  return {
    id: 'id-1',
    title: 'Untitled',
    createdAtUtc: '2026-07-29T10:00:00Z',
    modifiedAtUtc: '2026-07-29T10:05:00Z',
    isArchived: false,
    isPinned: false,
    isFavorite: false,
    isDeleted: false,
    ...overrides,
  }
}

function page(items: ConversationSummary[]): PagedResult<ConversationSummary> {
  return { items, nextCursor: null }
}

const server = setupServer()
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// Same jsdom-layout workaround as ChatSidebar.a11y.test.tsx / ConversationSwitcher.test.tsx.
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(400)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(56)
  useActiveConversationStore.setState({ activeChatId: null })
})

function LocationProbe() {
  const location = useLocation()
  return (
    <div data-testid="location">
      {location.pathname}
      {JSON.stringify(location.state)}
    </div>
  )
}

function renderTab() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/settings']}>
        <Routes>
          <Route
            path="*"
            element={
              <>
                <ChatHistoryTab />
                <LocationProbe />
              </>
            }
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('ChatHistoryTab', () => {
  it('lists conversations from the search API, matching the prior in-workspace list (FR-006)', async () => {
    server.use(
      http.get('*/api/v1/chats*', () => HttpResponse.json(page([makeChat({ id: 'a', title: 'Trip planning' })]))),
    )
    renderTab()

    expect(await screen.findByText('Trip planning')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'New chat' })).toBeInTheDocument()
    expect(screen.getByLabelText('Search conversations')).toBeInTheDocument()
    for (const label of ['All', 'Favorites', 'Pinned', 'Archived', 'Recently Deleted']) {
      expect(screen.getByText(label)).toBeInTheDocument()
    }
  })

  it('selecting a conversation sets the active conversation and navigates to the workspace (FR-007)', async () => {
    server.use(
      http.get('*/api/v1/chats*', () => HttpResponse.json(page([makeChat({ id: 'a', title: 'Trip planning' })]))),
    )
    const user = userEvent.setup()
    renderTab()

    await user.click(await screen.findByTestId('conversation-title'))

    expect(useActiveConversationStore.getState().activeChatId).toBe('a')
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/studio'))
  })

  it('starting a new chat clears the active conversation and navigates to the workspace', async () => {
    server.use(http.get('*/api/v1/chats*', () => HttpResponse.json(page([]))))
    useActiveConversationStore.setState({ activeChatId: 'previous-chat' })
    const user = userEvent.setup()
    renderTab()

    await user.click(await screen.findByRole('button', { name: 'New chat' }))

    expect(useActiveConversationStore.getState().activeChatId).toBeNull()
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('/studio'))
  })

  it('shows the "no conversations yet" empty state when there are none', async () => {
    server.use(http.get('*/api/v1/chats*', () => HttpResponse.json(page([]))))
    renderTab()

    expect(await screen.findByText('No conversations yet')).toBeInTheDocument()
  })

  it('filtering to Pinned re-queries the search API with the pinned filter', async () => {
    server.use(http.get('*/api/v1/chats*', () => HttpResponse.json(page([]))))
    const user = userEvent.setup()
    renderTab()
    await screen.findByText('No conversations yet')

    let capturedUrl: string | undefined
    server.use(
      http.get('*/api/v1/chats*', ({ request }) => {
        capturedUrl = request.url
        return HttpResponse.json(page([]))
      }),
    )
    await user.click(screen.getByText('Pinned'))

    await waitFor(() => expect(capturedUrl).toContain('pinned=true'))
  })
})
