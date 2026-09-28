import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { NotificationItem } from '../api/notificationsApi'
import { NotificationsPage } from './NotificationsPage'

expect.extend(toHaveNoViolations)

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

const server = setupServer(http.get('*/api/v1/notifications', () => HttpResponse.json({ items: [item], nextCursor: null })))

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// jsdom reports zero layout size, so @tanstack/react-virtual would otherwise compute zero
// visible rows (mirrors ChatSidebar.a11y.test.tsx).
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(72)
})

describe('NotificationsPage accessibility (specs/067)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { findByText, container } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/notifications']}>
          <Routes>
            <Route path="/notifications" element={<NotificationsPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('Document ready')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
