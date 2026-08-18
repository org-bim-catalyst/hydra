import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ConversationSummary, PagedResult } from '../api/chatsApi'
import { ConversationSwitcher } from './ConversationSwitcher'

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

// Same jsdom-layout workaround as ChatSidebar.a11y.test.tsx / ChatPage.test.tsx.
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(400)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(56)
})

function renderSwitcher(selectedChatId: string | null = null) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onSelectChat = vi.fn()
  const onNewChat = vi.fn()
  const utils = render(
    <QueryClientProvider client={queryClient}>
      <ConversationSwitcher
        selectedChatId={selectedChatId}
        onSelectChat={onSelectChat}
        onNewChat={onNewChat}
      />
    </QueryClientProvider>,
  )
  return { ...utils, onSelectChat, onNewChat }
}

describe('ConversationSwitcher', () => {
  it('opens the popover and lists conversations from the search API (FR-008)', async () => {
    server.use(
      http.get('*/api/v1/chats*', () =>
        HttpResponse.json(page([makeChat({ id: 'a', title: 'Trip planning' })])),
      ),
    )
    const user = userEvent.setup()
    renderSwitcher()

    await user.click(screen.getByRole('button', { name: 'Conversations' }))

    expect(await screen.findByText('Trip planning')).toBeInTheDocument()
  })

  it('selecting a conversation calls onSelectChat and closes the popover (FR-009)', async () => {
    server.use(
      http.get('*/api/v1/chats*', () =>
        HttpResponse.json(page([makeChat({ id: 'a', title: 'Trip planning' })])),
      ),
    )
    const user = userEvent.setup()
    const { onSelectChat } = renderSwitcher()

    await user.click(screen.getByRole('button', { name: 'Conversations' }))
    await user.click(await screen.findByTestId('conversation-title'))

    expect(onSelectChat).toHaveBeenCalledWith('a')
    await waitFor(() => expect(screen.queryByText('Trip planning')).not.toBeInTheDocument())
  })

  it('shows an empty state inviting a new conversation when there are none (US3 AC3)', async () => {
    server.use(http.get('*/api/v1/chats*', () => HttpResponse.json(page([]))))
    const user = userEvent.setup()
    renderSwitcher()

    await user.click(screen.getByRole('button', { name: 'Conversations' }))

    expect(await screen.findByText('No conversations yet')).toBeInTheDocument()
    expect(screen.getByText('New chat')).toBeInTheDocument()
  })

  it('keeps the virtualized list working with many conversations inside the bounded popover', async () => {
    const items = Array.from({ length: 40 }, (_, i) =>
      makeChat({ id: `id-${i}`, title: `Conversation ${i}` }),
    )
    server.use(http.get('*/api/v1/chats*', () => HttpResponse.json(page(items))))
    const user = userEvent.setup()
    renderSwitcher()

    await user.click(screen.getByRole('button', { name: 'Conversations' }))

    expect(await screen.findByText('Conversation 0')).toBeInTheDocument()
    // Bounded (fixed-height) Popover — a virtualized list mounts far fewer than 40 rows.
    expect(screen.queryAllByTestId('conversation-item').length).toBeLessThan(items.length)
  })
})
