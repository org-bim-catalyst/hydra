import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ConversationSummary, PagedResult } from '../../chat/api/chatsApi'
import { ChatHistoryTab } from './ChatHistoryTab'

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
]

const page: PagedResult<ConversationSummary> = { items: chats, nextCursor: null }

const server = setupServer(http.get('*/api/v1/chats*', () => HttpResponse.json(page)))

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// Same jsdom-layout workaround as ChatSidebar.a11y.test.tsx.
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(56)
})

describe('ChatHistoryTab accessibility', () => {
  it('has no automatically detectable a11y violations (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByLabelText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <ChatHistoryTab />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByLabelText('Search conversations')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
