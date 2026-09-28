import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import type { NotificationItem as NotificationItemDto } from '../api/notificationsApi'
import { NotificationItem } from './NotificationItem'

const baseItem: NotificationItemDto = {
  id: 'notif-1',
  category: 'Document',
  type: 'document.processed',
  title: 'Document ready',
  message: 'Your document <b>report.pdf</b> finished processing.',
  priority: 'Normal',
  status: 'Delivered',
  language: 'en',
  createdAtUtc: new Date().toISOString(),
  readAtUtc: null,
  expiresAtUtc: null,
  action: { label: 'Open document', route: '/documents/doc-1' },
  relatedItem: { type: 'document', id: 'doc-1', available: true },
}

function renderItem(item: NotificationItemDto, onOpen = vi.fn()) {
  return render(
    <MemoryRouter>
      <NotificationItem item={item} onOpen={onOpen} />
    </MemoryRouter>,
  )
}

describe('NotificationItem (specs/067 contracts/notifications-api.md — plain text only)', () => {
  it('renders title and message as literal text, never as HTML', () => {
    renderItem(baseItem)

    // A literal "<b>" in the message must show up as text, not create a <b> element.
    expect(screen.getByText('Your document <b>report.pdf</b> finished processing.')).toBeInTheDocument()
    expect(document.querySelector('b')).not.toBeInTheDocument()
  })

  it('shows an unread marker when readAtUtc is null', () => {
    renderItem(baseItem)
    expect(screen.getByRole('status', { name: 'Unread' })).toBeInTheDocument()
  })

  it('shows no unread marker once read', () => {
    renderItem({ ...baseItem, readAtUtc: new Date().toISOString() })
    expect(screen.queryByRole('status', { name: 'Unread' })).not.toBeInTheDocument()
  })

  it('shows "No longer available" instead of navigating when the related item is gone', () => {
    renderItem({ ...baseItem, relatedItem: { type: 'document', id: 'doc-1', available: false } })
    expect(screen.getByText('No longer available')).toBeInTheDocument()
  })

  it('calls onOpen when clicked', async () => {
    const onOpen = vi.fn()
    renderItem(baseItem, onOpen)
    await userEvent.click(screen.getByRole('button'))
    expect(onOpen).toHaveBeenCalledWith(baseItem)
  })
})
