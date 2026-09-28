import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { NotificationBell } from './NotificationBell'

const server = setupServer(
  http.get('*/api/v1/notifications/unread-count', () => HttpResponse.json({ count: 3 })),
  http.get('*/api/v1/notifications', () => HttpResponse.json({ items: [], nextCursor: null })),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderBell() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <NotificationBell />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('NotificationBell (T071)', () => {
  it('shows the unread count in the badge and the aria-label', async () => {
    renderBell()

    await waitFor(() => expect(screen.getByText('3')).toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'Notifications, 3 unread' })).toBeInTheDocument()
  })

  it('falls back to a plain "Notifications" label when there is nothing unread', async () => {
    server.use(http.get('*/api/v1/notifications/unread-count', () => HttpResponse.json({ count: 0 })))
    renderBell()

    await waitFor(() => expect(screen.getByRole('button', { name: 'Notifications' })).toBeInTheDocument())
  })
})
