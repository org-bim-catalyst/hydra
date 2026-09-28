import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { NotificationItem } from '../api/notificationsApi'
import { NotificationPopover } from './NotificationPopover'

const item: NotificationItem = {
  id: 'notif-1',
  category: 'Workflow',
  type: 'workflow.completed',
  title: 'Workflow finished',
  message: 'Your workflow run completed successfully.',
  priority: 'Normal',
  status: 'Delivered',
  language: 'en',
  createdAtUtc: new Date().toISOString(),
  readAtUtc: null,
  expiresAtUtc: null,
  action: null,
  relatedItem: null,
}

const server = setupServer(
  http.get('*/api/v1/notifications', () => HttpResponse.json({ items: [item], nextCursor: null })),
  http.post('*/api/v1/notifications/actions/mark-all-read', () => HttpResponse.json({ updated: 1 })),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPopover(anchorEl: HTMLElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <NotificationPopover anchorEl={anchorEl} onClose={() => {}} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('NotificationPopover (T071)', () => {
  it('lists the latest notifications and a "View all" link to the full page', async () => {
    renderPopover(document.body)

    expect(await screen.findByText('Workflow finished')).toBeInTheDocument()
    // getByRole crashes here (jsdom_getcomputedstyle_crash_mui_dialog): the popover is a portal,
    // so plain text lookups are used instead of role-based queries.
    expect(screen.getByText('View all').closest('a')).toHaveAttribute('href', '/notifications')
  })

  it('shows an error toast when mark-all-read fails', async () => {
    server.use(
      http.post('*/api/v1/notifications/actions/mark-all-read', () =>
        HttpResponse.json({ title: 'Server error', status: 500 }, { status: 500 }),
      ),
    )
    renderPopover(document.body)

    await screen.findByText('Workflow finished')
    screen.getByText('Mark all read').click()

    await waitFor(() => expect(screen.getByText(/couldn't|error|failed/i)).toBeInTheDocument())
  })
})
