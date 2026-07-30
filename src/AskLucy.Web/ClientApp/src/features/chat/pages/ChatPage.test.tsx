import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PagedResult, PersistedMessage } from '../api/chatsApi'
import { ConversationView } from './ChatPage'

const CHAT_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const CHAT_B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'

function makeMessage(overrides: Partial<PersistedMessage>): PersistedMessage {
  return {
    id: 'msg-1',
    role: 'Assistant',
    kind: 'Text',
    content: 'Hello there',
    sourceText: null,
    createdAtUtc: '2026-07-29T10:00:00Z',
    provider: 'OpenAI',
    model: 'gpt-4',
    generationParametersJson: null,
    inputTokenCount: null,
    outputTokenCount: null,
    attachments: [],
    citations: [],
    ...overrides,
  }
}

function messagesPage(items: PersistedMessage[]): PagedResult<PersistedMessage> {
  return { items, nextCursor: null }
}

/** Builds an SSE-formatted ReadableStream matching aiApi.ts's parser (`data: {chunk}\n\n`, ending `data: [DONE]\n\n`). */
function sseStream(chunks: string[], firstChunkDelayMs = 0): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder()
  return new ReadableStream({
    async start(controller) {
      if (firstChunkDelayMs > 0) {
        await new Promise((resolve) => setTimeout(resolve, firstChunkDelayMs))
      }
      for (const chunk of chunks) {
        controller.enqueue(encoder.encode(`data: ${chunk}\n\n`))
      }
      controller.enqueue(encoder.encode('data: [DONE]\n\n'))
      controller.close()
    },
  })
}

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// jsdom reports zero layout size, so @tanstack/react-virtual would otherwise compute zero
// visible rows — give the scroll container a plausible height so the virtualized list
// actually renders its items (same workaround as ChatSidebar.a11y.test.tsx).
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(56)
  // jsdom does not implement scrollIntoView at all (spyOn requires an existing property);
  // ConversationView calls it on every messages change.
  HTMLElement.prototype.scrollIntoView = vi.fn()
})

function renderConversation(chatId: string | null) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <ConversationView
          chatId={chatId}
          language="en"
          onLanguageChange={() => {}}
          onChatCreated={() => {}}
          isMobile={false}
          onOpenSidebar={() => {}}
        />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('ConversationView — conversation loading (User Story 1 & 2)', () => {
  it('shows the empty-state placeholder when no conversation is selected', () => {
    renderConversation(null)
    expect(screen.getByText('Start a conversation with Ask Lucy.')).toBeInTheDocument()
  })

  it('never shows the empty-state placeholder while the selected conversation is pending', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, async () => {
        await delay('infinite')
        return HttpResponse.json(messagesPage([]))
      }),
    )
    renderConversation(CHAT_A)

    expect(await screen.findByRole('status', { name: 'Loading conversation…' })).toBeInTheDocument()
    expect(screen.queryByText('Start a conversation with Ask Lucy.')).not.toBeInTheDocument()
  })

  it('shows a loading spinner when a conversation is selected and its messages are pending', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, async () => {
        await delay('infinite')
        return HttpResponse.json(messagesPage([]))
      }),
    )
    renderConversation(CHAT_A)

    expect(await screen.findByRole('status', { name: 'Loading conversation…' })).toBeInTheDocument()
  })

  it('replaces the loading spinner with the conversation messages once the fetch resolves', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () =>
        HttpResponse.json(messagesPage([makeMessage({ id: 'm1', content: 'Hi from history' })])),
      ),
    )
    renderConversation(CHAT_A)

    expect(await screen.findByText('Hi from history')).toBeInTheDocument()
    expect(screen.queryByRole('status', { name: 'Loading conversation…' })).not.toBeInTheDocument()
  })

  it('shows a visible error state with a Retry button when the selected conversation fails to load', async () => {
    server.use(http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => new HttpResponse(null, { status: 500 })))
    renderConversation(CHAT_A)

    expect(await screen.findByRole('alert')).toHaveTextContent('Failed to load this conversation')
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(screen.queryByText('Start a conversation with Ask Lucy.')).not.toBeInTheDocument()
  })

  it('clicking Retry re-fetches and shows the conversation messages after a prior load failure', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => new HttpResponse(null, { status: 500 }), { once: true }),
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () =>
        HttpResponse.json(messagesPage([makeMessage({ id: 'm1', content: 'Recovered message' })])),
      ),
    )
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    await screen.findByRole('button', { name: 'Retry' })
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByText('Recovered message')).toBeInTheDocument()
  })

  it('shows the last-selected conversation when clicking B before A resolves (FR-005)', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, async () => {
        await new Promise((resolve) => setTimeout(resolve, 100))
        return HttpResponse.json(messagesPage([makeMessage({ id: 'a1', content: 'Conversation A content' })]))
      }),
      http.get(`*/api/v1/chats/${CHAT_B}/messages`, () =>
        HttpResponse.json(messagesPage([makeMessage({ id: 'b1', content: 'Conversation B content' })])),
      ),
    )

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { rerender } = render(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <ConversationView
            key={CHAT_A}
            chatId={CHAT_A}
            language="en"
            onLanguageChange={() => {}}
            onChatCreated={() => {}}
            isMobile={false}
            onOpenSidebar={() => {}}
          />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    // Simulate the ChatPage remount-via-key behavior of selecting a second conversation
    // before the first one's slower fetch has resolved.
    rerender(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <ConversationView
            key={CHAT_B}
            chatId={CHAT_B}
            language="en"
            onLanguageChange={() => {}}
            onChatCreated={() => {}}
            isMobile={false}
            onOpenSidebar={() => {}}
          />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    expect(await screen.findByText('Conversation B content')).toBeInTheDocument()
    // Give A's slower response a chance to resolve, then confirm it never overwrote B's view.
    await new Promise((resolve) => setTimeout(resolve, 150))
    expect(screen.queryByText('Conversation A content')).not.toBeInTheDocument()
    expect(screen.getByText('Conversation B content')).toBeInTheDocument()
  })
})

describe('ConversationView — thinking indicator & send retry (User Story 3)', () => {
  it('shows ThinkingIndicator instead of a message bubble while no reply content has streamed in yet', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => HttpResponse.json(messagesPage([]))),
      http.post('*/api/v1/ai/chat', () => {
        const stream = sseStream(['Hello'], 100)
        return new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream' } })
      }),
    )
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    await user.type(screen.getByPlaceholderText('Message Ask Lucy...'), 'Hi Lucy')
    await user.click(screen.getByRole('button', { name: 'Send message' }))

    expect(await screen.findByRole('status', { name: 'Ask Lucy is thinking' })).toBeInTheDocument()
  })

  it('replaces ThinkingIndicator with streamed content once the first chunk arrives', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => HttpResponse.json(messagesPage([]))),
      http.post('*/api/v1/ai/chat', () => {
        const stream = sseStream(['Hello there'], 20)
        return new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream' } })
      }),
    )
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    await user.type(screen.getByPlaceholderText('Message Ask Lucy...'), 'Hi Lucy')
    await user.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(screen.getByText('Hello there')).toBeInTheDocument())
    expect(screen.queryByRole('status', { name: 'Ask Lucy is thinking' })).not.toBeInTheDocument()
  })

  it('surfaces a Retry-able Snackbar error on a failed send and resends the same content', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => HttpResponse.json(messagesPage([]))),
      http.post('*/api/v1/ai/chat', () => new HttpResponse(null, { status: 500 }), { once: true }),
      http.post('*/api/v1/ai/chat', () => {
        const stream = sseStream(['Recovered reply'])
        return new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream' } })
      }),
    )
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    await user.type(screen.getByPlaceholderText('Message Ask Lucy...'), 'Hi Lucy')
    await user.click(screen.getByRole('button', { name: 'Send message' }))

    await screen.findByRole('button', { name: 'Retry' })
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    await waitFor(() => expect(screen.getByText('Recovered reply')).toBeInTheDocument())
  })
})

describe('ConversationView — reopening a newly-created conversation (User Story 5)', () => {
  it('shows real messages when reopened after its first read captured an empty/stale snapshot', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => HttpResponse.json(messagesPage([])), { once: true }),
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () =>
        HttpResponse.json(
          messagesPage([
            makeMessage({ id: 'u1', role: 'User', content: 'Hi Lucy' }),
            makeMessage({ id: 'a1', content: 'Hello! How can I help?' }),
          ]),
        ),
      ),
    )

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const renderAt = (viewKey: number) =>
      render(
        <MemoryRouter>
          <QueryClientProvider client={queryClient}>
            <ConversationView
              key={viewKey}
              chatId={CHAT_A}
              language="en"
              onLanguageChange={() => {}}
              onChatCreated={() => {}}
              isMobile={false}
              onOpenSidebar={() => {}}
            />
          </QueryClientProvider>
        </MemoryRouter>,
      )

    // First mount: captures the empty snapshot — matching a fetch that raced an in-progress
    // reply for a chat that was just auto-created mid-send.
    const first = renderAt(1)
    await waitFor(() => expect(queryClient.getQueryData(['chats', CHAT_A, 'messages'])).toBeDefined())
    first.unmount()

    // Second mount (user navigated away and back): the cache still holds the stale empty
    // result, but the background refetch should deliver — and this view should display —
    // the real, now-persisted messages, not stay blank forever.
    renderAt(2)

    expect(await screen.findByText('Hello! How can I help?')).toBeInTheDocument()
  })

  it('continues syncing later-arriving paginated pages into the displayed messages', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor')
        if (!cursor) {
          return HttpResponse.json({ items: [makeMessage({ id: 'p1', content: 'Page one message' })], nextCursor: 'page2' })
        }
        return HttpResponse.json({ items: [makeMessage({ id: 'p2', content: 'Page two message' })], nextCursor: null })
      }),
    )

    renderConversation(CHAT_A)

    expect(await screen.findByText('Page one message')).toBeInTheDocument()
    expect(await screen.findByText('Page two message')).toBeInTheDocument()
  })
})
