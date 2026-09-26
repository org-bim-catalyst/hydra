import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { ChatInvestigation } from '../../api/adminOperationalFailuresApi'
import { ChatInvestigationPage } from './ChatInvestigationPage'

const metadataOnly: ChatInvestigation = {
  chat: {
    id: 'chat-1',
    title: 'Budget review',
    owner: { id: 'user-ada', displayName: 'Ada Lovelace', email: 'ada@example.com', status: 'Active' },
    createdAtUtc: '2026-09-22T09:00:00Z',
    lastActivityUtc: '2026-09-22T10:15:00Z',
    messageCount: 2,
    deleted: false,
  },
  failurePoints: [
    { occurrenceId: 'occurrence-1', turnNumber: 1, occurredAtUtc: '2026-09-22T10:15:00Z', messageId: 'message-2' },
  ],
  transcript: null,
}

const withTranscript: ChatInvestigation = {
  ...metadataOnly,
  transcript: [
    { id: 'message-1', role: 'user', createdAtUtc: '2026-09-22T10:14:00Z', content: 'What is the budget?', isFailedTurn: false },
    { id: 'message-2', role: 'assistant', createdAtUtc: '2026-09-22T10:15:00Z', content: 'The budget is', isFailedTurn: true },
  ],
}

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions: ['admin.operational-failures.view'] }),
  ),
  http.get('*/api/v1/admin/operational-failures/incidents/:incidentId/chats/:chatId', () => HttpResponse.json(metadataOnly)),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/operational-failures/incident-1/chats/chat-1']}>
        <Routes>
          <Route path="/admin/operational-failures/:incidentId/chats/:chatId" element={<ChatInvestigationPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const contentNote = (_: string, element: Element | null) =>
  element?.tagName === 'DIV' &&
  element.textContent === 'Content is visible only to staff with View user content.' &&
  !Array.from(element.children).some((child) => child.textContent === element.textContent)

describe('ChatInvestigationPage (specs/074 US1)', () => {
  it('shows metadata and failure points, and says why content is hidden, without the content permission', async () => {
    renderPage()

    expect(await screen.findByText('Budget review')).toBeInTheDocument()
    expect(screen.getByText('Ada Lovelace').closest('a')).toHaveAttribute('href', '/admin/users?search=ada%40example.com')
    expect(screen.getByText(/Turn 1/)).toBeInTheDocument()
    expect(screen.getByText(contentNote)).toBeInTheDocument()
    expect(screen.queryByText('The budget is')).not.toBeInTheDocument()
  })

  it('renders the transcript with the failed turn highlighted', async () => {
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:incidentId/chats/:chatId', () =>
        HttpResponse.json(withTranscript),
      ),
    )
    renderPage()

    expect(await screen.findByText('What is the budget?')).toBeInTheDocument()
    const failed = screen.getByTestId('failed-turn')
    expect(failed).toHaveTextContent('The budget is')
    expect(failed).toHaveTextContent('Failed turn')
    expect(screen.queryByText(contentNote)).not.toBeInTheDocument()
  })

  it('is read-only: no composer, text box or edit, delete or download control', async () => {
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:incidentId/chats/:chatId', () =>
        HttpResponse.json(withTranscript),
      ),
    )
    renderPage()
    await screen.findByText('What is the budget?')

    expect(screen.queryByRole('textbox')).toBeNull()
    expect(screen.queryByRole('button', { name: /edit|delete|download|send|retry/i })).toBeNull()
  })

  it('says when the chat was deleted', async () => {
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:incidentId/chats/:chatId', () =>
        HttpResponse.json({ ...metadataOnly, chat: { ...metadataOnly.chat, deleted: true } }),
      ),
    )
    renderPage()

    expect(await screen.findByText('This chat was deleted.')).toBeInTheDocument()
    expect(screen.queryByText(contentNote)).not.toBeInTheDocument()
  })

  it('shows an inline error with a retry when the investigation cannot be loaded', async () => {
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:incidentId/chats/:chatId', () =>
        HttpResponse.json({ title: 'Not found', status: 404, detail: 'Not found.' }, { status: 404 }),
      ),
    )
    renderPage()

    expect(await screen.findByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
