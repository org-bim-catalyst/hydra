import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { ConversationView } from './ChatPage'

expect.extend(toHaveNoViolations)

const CHAT_ID = 'cccccccc-cccc-cccc-cccc-cccccccccccc'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(56)
  HTMLElement.prototype.scrollIntoView = vi.fn()
})

function renderConversation() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <ConversationView
          chatId={CHAT_ID}
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

describe('ConversationView accessibility (constitution §7, §10)', () => {
  it('has no automatically detectable a11y violations while the loading spinner is shown', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_ID}/messages`, async () => {
        await new Promise(() => {}) // never resolves — keeps the query pending for the whole test
      }),
    )
    const { container, findByRole } = renderConversation()

    await findByRole('status', { name: 'Loading conversation…' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in the error/Retry state', async () => {
    server.use(http.get(`*/api/v1/chats/${CHAT_ID}/messages`, () => new HttpResponse(null, { status: 500 })))
    const { container, findByRole } = renderConversation()

    await findByRole('alert')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
