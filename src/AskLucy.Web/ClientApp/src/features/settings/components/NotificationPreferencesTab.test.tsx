import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { NotificationPreferences } from '../../notifications/api/notificationPreferencesApi'
import { NotificationPreferencesTab } from './NotificationPreferencesTab'

const preferences: NotificationPreferences = {
  categories: [
    {
      category: 'Security',
      channels: [
        { channel: 'InApp', enabled: true, locked: true },
        { channel: 'Email', enabled: true, locked: true },
      ],
      frequency: 'Immediate',
      availableFrequencies: ['Immediate'],
    },
    {
      category: 'Workflow',
      channels: [
        { channel: 'InApp', enabled: true, locked: false },
        { channel: 'Email', enabled: true, locked: false },
      ],
      frequency: 'Immediate',
      availableFrequencies: ['Immediate'],
    },
    {
      category: 'Memory',
      channels: [{ channel: 'InApp', enabled: true, locked: false }],
      frequency: 'Immediate',
      availableFrequencies: ['Immediate'],
    },
  ],
}

const server = setupServer(http.get('*/api/v1/users/me/notification-preferences', () => HttpResponse.json(preferences)))

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderTab() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <NotificationPreferencesTab />
    </QueryClientProvider>,
  )
}

const workflowEmail = () => screen.findByRole('switch', { name: 'Workflows notifications by Email' })

describe('NotificationPreferencesTab (T137)', () => {
  it('shows a switch for every category and channel, with the frequency as Immediate only', async () => {
    renderTab()

    expect(await workflowEmail()).toBeChecked()
    expect(screen.getByRole('switch', { name: 'Workflows notifications by In-app' })).toBeChecked()
    expect(screen.getAllByText('Immediate')).toHaveLength(preferences.categories.length)
  })

  it('locks the mandatory switches, and announces why to a screen reader', async () => {
    renderTab()

    const locked = await screen.findByRole('switch', { name: 'Security notifications by Email' })
    expect(locked).toBeDisabled()
    expect(locked).toBeChecked()
    const describedBy = locked.getAttribute('aria-describedby')
    expect(describedBy).toBeTruthy()
    expect(document.getElementById(describedBy!)).toHaveTextContent("Required: these notifications can't be turned off.")
    expect(screen.getByRole('switch', { name: 'Workflows notifications by Email' })).toBeEnabled()
  })

  it('marks a channel a category does not use as unavailable, not as an empty cell', async () => {
    renderTab()

    expect(await screen.findByText('Memory notifications are not sent by Email')).toBeInTheDocument()
  })

  it('sends only the changed pair, and keeps the switch at the server-confirmed value', async () => {
    const bodies: unknown[] = []
    server.use(
      http.put('*/api/v1/users/me/notification-preferences', async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json({
          categories: preferences.categories.map((c) =>
            c.category === 'Workflow'
              ? { ...c, channels: c.channels.map((ch) => (ch.channel === 'Email' ? { ...ch, enabled: false } : ch)) }
              : c,
          ),
        })
      }),
    )
    renderTab()

    await userEvent.click(await workflowEmail())

    await waitFor(() => expect(bodies).toEqual([{ changes: [{ category: 'Workflow', channel: 'Email', enabled: false }] }]))
    await waitFor(() => expect(screen.getByRole('switch', { name: 'Workflows notifications by Email' })).not.toBeChecked())
  })

  it('shows an error toast and puts the switch back when the save fails', async () => {
    server.use(
      http.put('*/api/v1/users/me/notification-preferences', () =>
        HttpResponse.json({ title: 'Server error', detail: 'The database is unavailable.', status: 500 }, { status: 500 }),
      ),
    )
    renderTab()

    await userEvent.click(await workflowEmail())

    expect(await screen.findByText("Your change wasn't saved. The database is unavailable.")).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('switch', { name: 'Workflows notifications by Email' })).toBeChecked())
  })

  it('shows the 422 reason when the server refuses a locked pair', async () => {
    server.use(
      http.put('*/api/v1/users/me/notification-preferences', () =>
        HttpResponse.json(
          { title: 'Notification preference locked', detail: 'One or more notification preferences cannot be changed.', status: 422 },
          { status: 422 },
        ),
      ),
    )
    renderTab()

    await userEvent.click(await workflowEmail())

    expect(await screen.findByText(/Your change wasn't saved\. One or more notification preferences/)).toBeInTheDocument()
  })

  it('shows a retry when the preferences fail to load', async () => {
    server.use(
      http.get('*/api/v1/users/me/notification-preferences', () =>
        HttpResponse.json({ title: 'Server error', detail: 'Could not load.', status: 500 }, { status: 500 }),
      ),
    )
    renderTab()

    expect(await screen.findByText('Could not load.')).toBeInTheDocument()
    server.use(http.get('*/api/v1/users/me/notification-preferences', () => HttpResponse.json(preferences)))
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await workflowEmail()).toBeInTheDocument()
  })
})
