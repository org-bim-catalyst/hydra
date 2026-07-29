import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ConversationSummary, PagedResult } from '../api/chatsApi'
import { ChatSidebar } from './ChatSidebar'

expect.extend(toHaveNoViolations)

const chats: ConversationSummary[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Quarterly budget planning',
    createdAtUtc: '2026-07-29T10:00:00Z',
    modifiedAtUtc: '2026-07-29T10:05:00Z',
    isArchived: false,
    isPinned: true,
    isFavorite: false,
    isDeleted: false,
  },
  {
    id: '22222222-2222-2222-2222-222222222222',
    title: 'Weekend trip ideas',
    createdAtUtc: '2026-07-28T09:00:00Z',
    modifiedAtUtc: null,
    isArchived: false,
    isPinned: false,
    isFavorite: true,
    isDeleted: false,
  },
]

const page: PagedResult<ConversationSummary> = { items: chats, nextCursor: null }

const server = setupServer(http.get('*/api/v1/chats*', () => HttpResponse.json(page)))

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// jsdom reports zero layout size, so @tanstack/react-virtual would otherwise compute zero
// visible rows — give the scroll container a plausible height so the virtualized list
// actually renders its items for this test to inspect.
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(56)
})

describe('ChatSidebar accessibility', () => {
  it('has no automatically detectable a11y violations (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByLabelText } = render(
      <QueryClientProvider client={queryClient}>
        <ChatSidebar selectedChatId={null} onSelectChat={() => {}} onNewChat={() => {}} />
      </QueryClientProvider>,
    )

    // The virtualized row list doesn't measure real layout under jsdom (no ResizeObserver/
    // real getBoundingClientRect), so this waits for the sidebar's static chrome — search,
    // filters, sort — to settle rather than a specific virtualized row, and checks a11y on
    // whatever is actually present. Row-content a11y is exercised indirectly via
    // MessageBubble's own a11y coverage.
    await findByLabelText('Search conversations')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
