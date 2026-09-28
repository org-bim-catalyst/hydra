import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { NotificationDetail, NotificationItem } from '../api/notificationsApi'
import { NotificationsPage } from './NotificationsPage'

const item: NotificationItem = {
  id: 'notif-1',
  category: 'Document',
  type: 'document.processed',
  title: 'Document ready',
  message: 'Your document finished processing.',
  priority: 'Normal',
  status: 'Delivered',
  language: 'en',
  createdAtUtc: new Date().toISOString(),
  readAtUtc: null,
  expiresAtUtc: null,
  action: null,
  relatedItem: null,
}

const detail: NotificationDetail = { ...item, metadata: { documentId: 'doc-1' } }

const server = setupServer(
  http.get('*/api/v1/notifications', () => HttpResponse.json({ items: [item], nextCursor: null })),
  http.get('*/api/v1/notifications/notif-1', () => HttpResponse.json(detail)),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// jsdom reports zero layout size, so @tanstack/react-virtual would otherwise compute zero
// visible rows — give the scroll container a plausible height so NotificationList's virtualized
// items actually render (mirrors ChatSidebar.a11y.test.tsx).
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(72)
})

function renderPage(initialEntry: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <Routes>
          <Route path="/notifications" element={<NotificationsPage />} />
          <Route path="/notifications/:id" element={<NotificationsPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('NotificationsPage (T072)', () => {
  it('lists notifications from the API', async () => {
    renderPage('/notifications')
    expect(await screen.findByText('Document ready')).toBeInTheDocument()
  })

  it('opens the details drawer when the route carries an id', async () => {
    renderPage('/notifications/notif-1')

    await waitFor(() => expect(screen.getAllByText('Document ready').length).toBeGreaterThan(1))
    expect(screen.getByText('documentId: doc-1')).toBeInTheDocument()
  })

  it('does not show the details drawer when there is no id in the route', async () => {
    renderPage('/notifications')
    await screen.findByText('Document ready')
    expect(screen.queryByText(/documentId/)).not.toBeInTheDocument()
  })
})
