import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type {
  NotificationTemplateDetail,
  TemplateVersion,
} from '../api/adminNotificationTemplatesApi'
import { templateTextProblem } from '../forms/templateText'
import { AdminNotificationTemplateEditorPage } from './AdminNotificationTemplateEditorPage'

const detail: NotificationTemplateDetail = {
  templateId: 't1',
  type: 'workflow.execution.failed',
  category: 'Workflow',
  channel: 'Email',
  language: 'en',
  name: 'Workflow failed (email)',
  publishedVersionId: 'v1',
  isShippedDefault: true,
  versions: [
    {
      id: 'v2',
      versionNumber: 2,
      status: 'Draft',
      createdAtUtc: '2026-10-05T00:00:00Z',
      createdBy: 'Ada Lovelace',
      publishedAtUtc: null,
      archivedAtUtc: null,
    },
    {
      id: 'v1',
      versionNumber: 1,
      status: 'Published',
      createdAtUtc: '2026-10-01T00:00:00Z',
      createdBy: 'System',
      publishedAtUtc: '2026-10-01T00:00:00Z',
      archivedAtUtc: null,
    },
  ],
  declaredVariables: [
    { name: 'workflowName', sample: 'your workflow', fallback: 'your workflow', isStandard: false },
    {
      name: 'failureSummary',
      sample: 'an unexpected error',
      fallback: 'an unexpected error',
      isStandard: false,
    },
    { name: 'recipientDisplayName', sample: 'there', fallback: 'there', isStandard: true },
  ],
}

const version = (
  id: string,
  number: number,
  status: TemplateVersion['status'],
  rowVersion: string,
): TemplateVersion => ({
  id,
  templateId: 't1',
  versionNumber: number,
  status,
  rowVersion,
  subject: '{{ workflowName }} failed',
  preheader: null,
  greeting: 'Hello {{ recipientDisplayName }}',
  heading: 'It failed',
  bodyParagraphs: ['Reason: {{ failureSummary }}'],
  actionLabel: 'Open',
  safetyNote: 'Ignore this if it was not you.',
  footerNote: null,
  title: null,
  message: null,
  usedVariables: ['workflowName', 'failureSummary'],
  createdAtUtc: '2026-10-05T00:00:00Z',
  publishedAtUtc: null,
  archivedAtUtc: null,
})

let permissions = ['admin.notifications.view', 'admin.notifications.manage']
let putResponse: () => Response = () => HttpResponse.json(version('v2', 2, 'Draft', 'cm93LXYy'))
let publishResponse: () => Response = () =>
  HttpResponse.json(version('v2', 2, 'Published', 'cm93LXYz'))
const puts: { ifMatch: string | null; body: unknown }[] = []
const publishes: (string | null)[] = []

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['User'], permissions }),
  ),
  http.get('*/api/v1/admin/notifications/templates/t1', () => HttpResponse.json(detail)),
  http.get('*/api/v1/admin/notifications/templates/t1/versions/v2', () =>
    HttpResponse.json(version('v2', 2, 'Draft', 'cm93LXYy')),
  ),
  http.get('*/api/v1/admin/notifications/templates/t1/versions/v1', () =>
    HttpResponse.json(version('v1', 1, 'Published', 'cm93LXYx')),
  ),
  http.post('*/api/v1/admin/notifications/templates/t1/versions/:versionId/actions/preview', () =>
    HttpResponse.json({
      subject: 'your workflow failed',
      html: '<html><body><h1>RENDERED HEADING</h1><script>alert(1)</script></body></html>',
      text: 'text',
      title: null,
      message: null,
      actionLabel: null,
      language: 'en',
      direction: 'ltr',
    }),
  ),
  http.put('*/api/v1/admin/notifications/templates/t1/versions/v2', async ({ request }) => {
    puts.push({ ifMatch: request.headers.get('If-Match'), body: await request.json() })
    return putResponse()
  }),
  http.post(
    '*/api/v1/admin/notifications/templates/t1/versions/v2/actions/publish',
    ({ request }) => {
      publishes.push(request.headers.get('If-Match'))
      return publishResponse()
    },
  ),
  http.post('*/api/v1/admin/notifications/templates/t1/versions/v2/actions/send-test', () =>
    HttpResponse.json({ sentTo: 'a•••@example.com' }, { status: 202 }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  permissions = ['admin.notifications.view', 'admin.notifications.manage']
  putResponse = () => HttpResponse.json(version('v2', 2, 'Draft', 'cm93LXYy'))
  publishResponse = () => HttpResponse.json(version('v2', 2, 'Published', 'cm93LXYz'))
  puts.length = 0
  publishes.length = 0
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createMemoryRouter(
    [
      {
        path: '/admin/notifications/templates/:templateId',
        element: <AdminNotificationTemplateEditorPage />,
      },
    ],
    {
      initialEntries: ['/admin/notifications/templates/t1'],
    },
  )
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
}

/** Role queries trip a jsdom bug once a dialog is open, so buttons are found by their text. */
const button = (text: string) => screen.getByText(text).closest('button') as HTMLButtonElement

describe('AdminNotificationTemplateEditorPage (specs/067 US7)', () => {
  it('opens the newest draft with the version list and the variable chips from declaredVariables', async () => {
    renderPage()

    expect(await screen.findByLabelText('Subject')).toHaveValue('{{ workflowName }} failed')
    const versions = within(screen.getByRole('list', { name: 'Versions' }))
    expect(versions.getByText('Version 2')).toBeInTheDocument()
    expect(versions.getByText('Version 1')).toBeInTheDocument()
    for (const name of ['workflowName', 'failureSummary', 'recipientDisplayName']) {
      expect(screen.getByRole('button', { name: `Insert ${name}` })).toBeInTheDocument()
    }
  })

  it('renders the preview in a sandboxed frame via srcDoc and never injects the HTML into the page', async () => {
    renderPage()

    const frame = (await screen.findByTitle('Email preview')) as HTMLIFrameElement
    expect(frame.tagName).toBe('IFRAME')
    expect(frame.getAttribute('sandbox')).toBe('')
    expect(frame.getAttribute('srcdoc')).toContain('RENDERED HEADING')
    expect(screen.queryByText('RENDERED HEADING')).not.toBeInTheDocument()
    expect(document.querySelector('script')).toBeNull()
  })

  it('inserts a variable at the cursor of the focused field', async () => {
    renderPage()
    const heading = await screen.findByLabelText('Heading')

    fireEvent.focus(heading)
    ;(heading as HTMLInputElement).setSelectionRange(2, 2)
    fireEvent.click(screen.getByRole('button', { name: 'Insert workflowName' }))

    await waitFor(() => expect(heading).toHaveValue('It{{ workflowName }} failed'))
  })

  it('validates unknown variables and raw links before sending anything', async () => {
    renderPage()
    const heading = await screen.findByLabelText('Heading')

    fireEvent.change(heading, { target: { value: 'Hello {{ nobody }}' } })
    expect(
      await screen.findByText("'{{ nobody }}' is not a variable this notification provides."),
    ).toBeInTheDocument()

    fireEvent.change(heading, { target: { value: 'See https://evil.example' } })
    expect(await screen.findByText(/Links and HTML are not allowed/)).toBeInTheDocument()
    expect(puts).toHaveLength(0)
  })

  it('saves with the row version as If-Match', async () => {
    renderPage()
    const heading = await screen.findByLabelText('Heading')

    fireEvent.change(heading, { target: { value: 'Changed {{ workflowName }}' } })
    await waitFor(() => expect(button('Save draft')).toBeEnabled())
    fireEvent.click(button('Save draft'))

    await waitFor(() => expect(puts).toHaveLength(1))
    expect(puts[0]?.ifMatch).toBe('cm93LXYy')
    expect(puts[0]?.body).toMatchObject({
      heading: 'Changed {{ workflowName }}',
      bodyParagraphs: ['Reason: {{ failureSummary }}'],
    })
  })

  it('shows a reload prompt on a 409 instead of overwriting', async () => {
    putResponse = () =>
      HttpResponse.json(
        {
          title: 'Template version conflict',
          detail: 'This template version was changed by someone else. Reload it and try again.',
          reason: 'ConcurrencyConflict',
        },
        { status: 409 },
      )
    renderPage()
    const heading = await screen.findByLabelText('Heading')

    fireEvent.change(heading, { target: { value: 'Changed {{ workflowName }}' } })
    await waitFor(() => expect(button('Save draft')).toBeEnabled())
    fireEvent.click(button('Save draft'))

    expect(await screen.findByText(/changed by someone else/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument()
  })

  it('publishes only after a confirmation, sending If-Match', async () => {
    renderPage()
    await screen.findByLabelText('Heading')

    fireEvent.click(button('Publish'))
    expect(await screen.findByText('Publish version 2?')).toBeInTheDocument()
    expect(publishes).toHaveLength(0)
    fireEvent.click(screen.getAllByText('Publish').at(-1)!.closest('button') as HTMLButtonElement)

    await waitFor(() => expect(publishes).toEqual(['cm93LXYy']))
    expect(await screen.findByText('Version 2 is now live.')).toBeInTheDocument()
  })

  it('shows a failed publish as a visible error', async () => {
    publishResponse = () =>
      HttpResponse.json(
        { title: 'Template rejected', detail: "'{{ nobody }}' is not a variable." },
        { status: 422 },
      )
    renderPage()
    await screen.findByLabelText('Heading')

    fireEvent.click(button('Publish'))
    await screen.findByText('Publish version 2?')
    fireEvent.click(screen.getAllByText('Publish').at(-1)!.closest('button') as HTMLButtonElement)

    expect(await screen.findByText(/The version wasn't published\./)).toBeInTheDocument()
  })

  it('reports a test send to the masked address', async () => {
    renderPage()
    await screen.findByLabelText('Heading')

    fireEvent.click(button('Send test to me'))

    expect(
      await screen.findByText('A test email was sent to a•••@example.com.'),
    ).toBeInTheDocument()
  })

  it('is read-only for a view-only administrator: no save, publish, archive or test send', async () => {
    permissions = ['admin.notifications.view']
    renderPage()
    const heading = await screen.findByLabelText('Heading')

    expect(heading).toHaveAttribute('readonly')
    expect(screen.queryByText('Save draft')).not.toBeInTheDocument()
    expect(screen.queryByText('Publish')).not.toBeInTheDocument()
    expect(screen.queryByText('Archive')).not.toBeInTheDocument()
    expect(screen.queryByText('Send test to me')).not.toBeInTheDocument()
    expect(screen.queryByText('New version')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Insert workflowName' })).not.toBeInTheDocument()
  })

  it('shows a published version as not editable, with a way to start a new one', async () => {
    renderPage()
    await screen.findByLabelText('Heading')

    fireEvent.click(screen.getByText('Version 1'))

    expect(await screen.findByText(/A published version can't be edited/)).toBeInTheDocument()
    expect(screen.getByLabelText('Heading')).toHaveAttribute('readonly')
    expect(screen.getByText('New version')).toBeInTheDocument()
  })
})

describe('templateTextProblem', () => {
  const allowed = new Set(['workflowName'])

  it.each([
    ['Hello {{ workflowName }}', null],
    ['Hello {{workflowName}}', null],
    ['Hello {{ nobody }}', "'{{ nobody }}' is not a variable this notification provides."],
    ['Hello {{ workflowName', "Unmatched '{{' or '}}'. Write each variable as {{ name }}."],
    ['Hello {{ a b }}', "'{{ a b }}' is not a valid variable. Use {{ name }}."],
    [
      'Hello {{ actionUrl }}',
      'The action link is added by the platform and cannot be placed in text.',
    ],
    ['<b>hi</b>', 'Links and HTML are not allowed. Links come only from the action button.'],
    [
      'go to www.example.com',
      'Links and HTML are not allowed. Links come only from the action button.',
    ],
  ])('%s', (value, expected) => {
    expect(templateTextProblem(value, 200, allowed)).toBe(expected)
  })

  it('enforces the length', () => {
    expect(templateTextProblem('x'.repeat(61), 60, allowed)).toBe('Can be at most 60 characters.')
  })
})
