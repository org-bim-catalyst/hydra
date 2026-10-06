import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AdminAnnouncement } from '../api/adminNotificationsApi'
import { AdminAnnouncementsPage } from './AdminAnnouncementsPage'

const announcement: AdminAnnouncement = {
  id: 'a1',
  kind: 'Maintenance',
  title: 'Scheduled maintenance',
  audience: 'AllActiveUsers',
  targetRoles: [],
  isCritical: true,
  endsAtUtc: '2026-10-07T23:00:00Z',
  publishedAtUtc: '2026-10-06T10:00:00Z',
  publishedBy: 'Ada Lovelace',
  recipientCount: 1234,
  fanOutStatus: 'Completed',
  emailQueued: 3,
  emailSent: 1200,
  emailExpired: 31,
}

let permissions = ['admin.notifications.view', 'admin.notifications.manage']
let items: AdminAnnouncement[] = [announcement]
const published: unknown[] = []

const server = setupServer(
  http.get('*/api/v1/auth/session', () => HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['User'], permissions })),
  http.get('*/api/v1/admin/notifications/announcements', () => HttpResponse.json({ items, nextCursor: null })),
  http.get('*/api/v1/admin/roles', () =>
    HttpResponse.json({ items: [{ id: 'role-1', name: 'Engineers' }, { id: 'role-2', name: 'Managers' }], totalCount: 2, page: 1, pageSize: 100 }),
  ),
  http.post('*/api/v1/admin/notifications/announcements', async ({ request }) => {
    published.push(await request.json())
    return HttpResponse.json({ id: 'a2', estimatedRecipients: 1234, emailEstimatedMinutes: 21 }, { status: 201 })
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  permissions = ['admin.notifications.view', 'admin.notifications.manage']
  items = [announcement]
  published.length = 0
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  const router = createMemoryRouter([{ path: '/admin/notifications/announcements', element: <AdminAnnouncementsPage /> }], {
    initialEntries: ['/admin/notifications/announcements'],
  })
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

/** Role queries trip a jsdom bug once a dialog is open, so buttons and fields are found by their text or label. */
const button = (text: string) => screen.getByText(text).closest('button') as HTMLButtonElement
const type = (label: string, value: string) => fireEvent.change(screen.getByLabelText(new RegExp(`^${label}`)), { target: { value } })

async function openDialog() {
  renderPage()
  await screen.findByText('Scheduled maintenance')
  fireEvent.click(button('New announcement'))
  await screen.findByLabelText(/^Title/)
}

describe('AdminAnnouncementsPage (T157, specs/067 US6)', () => {
  it('lists the announcements, with how many people and how much email each reached', async () => {
    renderPage()

    expect(await screen.findByText('Scheduled maintenance')).toBeInTheDocument()
    expect(screen.getByText('Critical')).toBeInTheDocument()
    expect(screen.getByText('1234')).toBeInTheDocument()
    expect(screen.getByText('1200 sent · 3 queued · 31 expired')).toBeInTheDocument()
    expect(screen.getByText('by Ada Lovelace')).toBeInTheDocument()
  })

  it('hides the publish control from an administrator who can only view', async () => {
    permissions = ['admin.notifications.view']
    renderPage()

    await screen.findByText('Scheduled maintenance')
    expect(screen.queryByText('New announcement')).not.toBeInTheDocument()
  })

  it('has no way to edit or delete a published announcement', async () => {
    renderPage()

    await screen.findByText('Scheduled maintenance')
    expect(screen.queryByText('Edit')).not.toBeInTheDocument()
    expect(screen.queryByText('Delete')).not.toBeInTheDocument()
  })

  it('says when there are none', async () => {
    items = []
    renderPage()

    expect(await screen.findByText('No announcements yet.')).toBeInTheDocument()
  })

  it('validates with the same rules as the server, and sends nothing while a field is wrong', async () => {
    await openDialog()

    fireEvent.click(button('Publish'))

    expect(await screen.findByText('Enter a title.')).toBeInTheDocument()
    expect(screen.getByText('Enter a message.')).toBeInTheDocument()
    expect(published).toHaveLength(0)
  })

  it('refuses links and HTML in the text', async () => {
    await openDialog()
    type('Title', 'Visit https://example.com')
    type('Message', '<b>Bold</b>')

    fireEvent.click(button('Publish'))

    expect(await screen.findAllByText('Links and HTML are not allowed.')).toHaveLength(2)
    expect(published).toHaveLength(0)
  })

  it('refuses a title that is too long', async () => {
    await openDialog()
    type('Title', 'a'.repeat(151))
    type('Message', 'Down for a bit.')

    fireEvent.click(button('Publish'))

    expect(await screen.findByText('The title can be at most 150 characters.')).toBeInTheDocument()
  })

  it('refuses an end time in the past', async () => {
    await openDialog()
    type('Title', 'Maintenance')
    type('Message', 'Down for a bit.')
    type('Ends', '2020-01-01T10:00')

    fireEvent.click(button('Publish'))

    expect(await screen.findByText('The end time must be in the future.')).toBeInTheDocument()
  })

  it('publishes an announcement that is not critical in one step, and reports who it reaches', async () => {
    await openDialog()
    type('Title', 'Release notes')
    type('Message', 'A new version is out.')

    fireEvent.click(button('Publish'))

    await waitFor(() => expect(published).toHaveLength(1))
    expect(published[0]).toMatchObject({ kind: 'Maintenance', title: 'Release notes', message: 'A new version is out.', audience: 'AllActiveUsers', targetRoleIds: null, isCritical: false, endsAtUtc: null })
    expect(await screen.findByText(/Published\. It reaches about 1234 people, and the emails take about 21 min to go out\./)).toBeInTheDocument()
  })

  it('asks a second time before publishing a critical one, and sends nothing until it is confirmed', async () => {
    await openDialog()
    type('Title', 'Outage')
    type('Message', 'The service is down.')
    fireEvent.click(screen.getByLabelText('Critical: also send by email'))

    fireEvent.click(button('Publish'))

    expect(await screen.findByText('Send this to everyone?')).toBeInTheDocument()
    expect(screen.getByText(/and by email/)).toBeInTheDocument()
    expect(published).toHaveLength(0)

    fireEvent.click(button('Publish to everyone'))
    await waitFor(() => expect(published).toHaveLength(1))
    expect(published[0]).toMatchObject({ isCritical: true })
  })

  it('goes back from the confirmation without sending anything', async () => {
    await openDialog()
    type('Title', 'Outage')
    type('Message', 'The service is down.')
    fireEvent.click(screen.getByLabelText('Critical: also send by email'))
    fireEvent.click(button('Publish'))
    await screen.findByText('Send this to everyone?')

    fireEvent.click(button('Back'))

    expect(await screen.findByText('New announcement', { selector: '#publish-announcement-title' })).toBeInTheDocument()
    expect(published).toHaveLength(0)
  })

  it('shows why a publish failed, and keeps the dialog open so nothing typed is lost', async () => {
    server.use(
      http.post('*/api/v1/admin/notifications/announcements', () =>
        HttpResponse.json({ title: 'Validation failed', detail: 'Role ghost does not exist.', status: 400 }, { status: 400 }),
      ),
    )
    await openDialog()
    type('Title', 'Release notes')
    type('Message', 'A new version is out.')

    fireEvent.click(button('Publish'))

    expect(await screen.findByText("The announcement wasn't published. Role ghost does not exist.")).toBeInTheDocument()
    expect((screen.getByLabelText(/^Title/) as HTMLInputElement).value).toBe('Release notes')
  })

  it('needs at least one role when the audience is specific roles', async () => {
    await openDialog()
    type('Title', 'Engineering update')
    type('Message', 'Please read.')
    fireEvent.mouseDown(screen.getByLabelText('Audience'))
    fireEvent.click(await screen.findByText('Specific roles'))

    fireEvent.click(button('Publish'))

    expect(await screen.findByText('Choose at least one role.')).toBeInTheDocument()
    expect(published).toHaveLength(0)
  })
})
