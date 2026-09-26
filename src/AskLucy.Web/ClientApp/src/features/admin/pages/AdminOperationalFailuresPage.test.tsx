import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, useLocation } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { PagedResult } from '../api/adminApi'
import type { IncidentDetail, IncidentSummary, Occurrence } from '../api/adminOperationalFailuresApi'
import { AdminOperationalFailuresPage } from './AdminOperationalFailuresPage'

const credentialIncident: IncidentSummary = {
  id: 'incident-1',
  rowVersion: 'AAAAAAAAB9E=',
  severity: 'Critical',
  engine: 'Chat',
  operation: 'Chat reply',
  kind: 'CredentialRejected',
  providerId: 'provider-openai',
  providerName: 'OpenAI',
  model: 'gpt-5',
  subject: null,
  firstSeenUtc: '2026-09-22T09:45:00Z',
  lastSeenUtc: '2026-09-22T10:15:00Z',
  occurrenceCount: 1200,
  storedOccurrenceCount: 1000,
  distinctUserCount: 4,
  distinctSourceCount: 0,
  recoveryCount: 0,
  latestReason: 'The provider rejected the credential',
  latestCorrelationId: 'corr-abc-123',
  state: 'Open',
  rootCauseKey: 'root-openai',
  relatedOpenCount: 0,
  isRecurrence: false,
}

const accessIncident: IncidentSummary = {
  ...credentialIncident,
  id: 'incident-access',
  severity: 'Warning',
  engine: 'Access',
  operation: 'Sign in',
  kind: 'SignInRefused',
  providerId: null,
  providerName: null,
  model: null,
  occurrenceCount: 9,
  storedOccurrenceCount: 9,
  distinctUserCount: 2,
  distinctSourceCount: 5,
  latestReason: 'The password was wrong',
  latestCorrelationId: 'corr-access',
  rootCauseKey: 'root-access',
}

const detail: IncidentDetail = {
  ...credentialIncident,
  recurrenceOfIncidentId: null,
  acknowledged: null,
  resolved: null,
  correctiveAction: {
    text: 'Replace the OpenAI credential',
    adminRoute: '/admin/ai-providers?select=provider-openai',
    adminAction: null,
  },
  providerHealth: { status: 'Unhealthy', failureKind: 'CredentialRejected', checkedAtUtc: '2026-09-22T10:00:00Z' },
  sampleUsers: [{ id: 'user-ada', displayName: 'Ada Lovelace', email: 'ada@example.com', status: 'Active' }],
  canManage: true,
  canViewContent: false,
}

const occurrence: Occurrence = {
  id: 'occurrence-1',
  occurredAtUtc: '2026-09-22T10:15:00Z',
  severity: 'Error',
  kind: 'CredentialRejected',
  reason: 'The provider rejected the credential',
  correlationId: 'corr-abc-123',
  isFailover: false,
  user: { id: 'user-grace', displayName: 'Grace Hopper', email: 'grace@example.com', status: 'Active' },
  chat: { id: 'chat-1', title: 'Budget review', deleted: false },
  messageId: 'message-1',
  workflow: null,
  document: null,
  agent: null,
  mcpServer: null,
  jobId: null,
  sourceIp: null,
}

const paged = <T,>(items: T[]): PagedResult<T> => ({ items, totalCount: items.length, page: 1, pageSize: 25 })

let lastListUrl: URL | undefined
let summaryCalls = 0

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: [],
      permissions: ['admin.operational-failures.view'],
    }),
  ),
  http.get('*/api/v1/admin/operational-failures/incidents', ({ request }) => {
    lastListUrl = new URL(request.url)
    return HttpResponse.json(paged([credentialIncident]))
  }),
  http.get('*/api/v1/admin/operational-failures/incidents/:id', ({ params }) =>
    params.id === accessIncident.id
      ? HttpResponse.json({
          ...detail,
          ...accessIncident,
          correctiveAction: { text: 'Review the account', adminRoute: null, adminAction: null },
          providerHealth: null,
        })
      : HttpResponse.json(detail),
  ),
  http.get('*/api/v1/admin/operational-failures/incidents/:id/occurrences', () => HttpResponse.json(paged([occurrence]))),
  http.get('*/api/v1/admin/operational-failures/summary', () => {
    summaryCalls += 1
    return HttpResponse.json({ unacknowledgedCriticalRootCauses: 1 })
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

/** Shows the router's current query string, so a test can read what the filter bar wrote. */
function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location-search">{location.search}</output>
}

function renderPage(initialEntry = '/admin/operational-failures') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <AdminOperationalFailuresPage />
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminOperationalFailuresPage (specs/074 US1)', () => {
  it('lists incidents with severity, engine, provider/model, kind, counts and last seen', async () => {
    renderPage()

    const row = (await screen.findByText('Chat reply')).closest('tr')!
    expect(within(row).getByText('Critical')).toBeInTheDocument()
    expect(within(row).getByText('Chat')).toBeInTheDocument()
    expect(within(row).getByText('OpenAI · gpt-5')).toBeInTheDocument()
    expect(within(row).getByText('Credential rejected')).toBeInTheDocument()
    expect(within(row).getByText('1200')).toBeInTheDocument()
    expect(within(row).getByText('showing 1000 of 1200')).toBeInTheDocument()
    expect(within(row).getByText('4')).toBeInTheDocument()
    expect(within(row).getByText(new Date(credentialIncident.lastSeenUtc).toLocaleString())).toBeInTheDocument()
  })

  it('asks for the default view: unresolved incidents from the last seven days', async () => {
    renderPage()
    await screen.findByText('Chat reply')

    expect(lastListUrl?.searchParams.get('state')).toBe('Unresolved')
    const from = new Date(lastListUrl!.searchParams.get('from')!).getTime()
    const sevenDays = 7 * 24 * 60 * 60 * 1000
    expect(Math.abs(Date.now() - sevenDays - from)).toBeLessThan(60_000)
    expect(lastListUrl?.searchParams.get('page')).toBe('1')
  })

  it('opens the incident drawer with the reason, correlation id, user, chat and corrective-action links', async () => {
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))

    expect(await screen.findByText('Replace the OpenAI credential')).toBeInTheDocument()
    expect(screen.getByText('Replace the OpenAI credential').closest('a')).toHaveAttribute(
      'href',
      '/admin/ai-providers?select=provider-openai',
    )
    expect(screen.getAllByText('The provider rejected the credential').length).toBeGreaterThan(0)
    expect(screen.getAllByText('corr-abc-123').length).toBeGreaterThan(0)
    expect(screen.getByLabelText('Copy correlation id')).toBeInTheDocument()
    expect(screen.getByText('Unhealthy')).toBeInTheDocument()

    expect(screen.getByText('Ada Lovelace').closest('a')).toHaveAttribute('href', '/admin/users?search=ada%40example.com')
    expect((await screen.findByText('Grace Hopper')).closest('a')).toHaveAttribute(
      'href',
      '/admin/users?search=grace%40example.com',
    )
    expect(screen.getByText('Budget review').closest('a')).toHaveAttribute(
      'href',
      '/admin/operational-failures/incident-1/chats/chat-1',
    )
  })

  it('shows an Access incident’s distinct sources and accounts', async () => {
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents', () => HttpResponse.json(paged([accessIncident]))),
    )
    renderPage()
    // The app header has its own 'Sign in' text, so wait for the row and click inside the table.
    await screen.findByText('Sign in refused')
    fireEvent.click(within(screen.getByRole('table', { name: 'Operational failure incidents' })).getByText('Sign in'))

    expect(await screen.findByText('5 sources · 2 accounts')).toBeInTheDocument()
  })

  it('shows an inline error with a retry when the list cannot be loaded', async () => {
    let calls = 0
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents', () => {
        calls += 1
        return calls === 1
          ? HttpResponse.json({ title: 'Server error', status: 500 }, { status: 500 })
          : HttpResponse.json(paged([credentialIncident]))
      }),
    )
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Retry' }))

    expect(await screen.findByText('Chat reply')).toBeInTheDocument()
    await waitFor(() => expect(calls).toBe(2))
  })

  it('shows an inline error with a retry when the incident detail cannot be loaded', async () => {
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:id', () =>
        HttpResponse.json({ title: 'Server error', status: 500 }, { status: 500 }),
      ),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))

    expect(await screen.findByText('Could not load this incident.')).toBeInTheDocument()
    expect(screen.getAllByText('Retry').length).toBeGreaterThan(0)
  })
})

describe('AdminOperationalFailuresPage (specs/074 US2)', () => {
  const burst: IncidentSummary = {
    ...credentialIncident,
    id: 'incident-burst',
    engine: 'Voice',
    operation: 'Text-to-speech',
    providerName: 'ElevenLabs',
    model: 'eleven_flash_v2_5',
    occurrenceCount: 7,
    storedOccurrenceCount: 7,
    distinctUserCount: 1,
    recoveryCount: 7,
    isRecurrence: true,
  }

  it('shows a burst as one row with its recoveries and marks a recurrence', async () => {
    server.use(http.get('*/api/v1/admin/operational-failures/incidents', () => HttpResponse.json(paged([burst]))))
    renderPage()

    const row = (await screen.findByText('Text-to-speech')).closest('tr')!
    expect(within(row).getByText('7')).toBeInTheDocument()
    expect(within(row).getByText('recovered 7×')).toBeInTheDocument()
    expect(within(row).getByText('Recurrence')).toBeInTheDocument()
  })

  it('links a recurrence back to the resolved incident it follows', async () => {
    const opened: string[] = []
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents', () => HttpResponse.json(paged([burst]))),
      http.get('*/api/v1/admin/operational-failures/incidents/:id', ({ params }) => {
        opened.push(String(params.id))
        return params.id === burst.id
          ? HttpResponse.json({ ...detail, ...burst, recurrenceOfIncidentId: 'incident-earlier' })
          : HttpResponse.json({ ...detail, ...burst, id: 'incident-earlier', operation: 'Earlier text-to-speech', isRecurrence: false })
      }),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Text-to-speech'))

    expect(await screen.findByText(/recovered 7×/)).toBeInTheDocument()
    fireEvent.click(await screen.findByText('Recurrence of an earlier resolved incident'))

    expect(await screen.findByText('Earlier text-to-speech')).toBeInTheDocument()
    expect(opened).toContain('incident-earlier')
  })
})

describe('AdminOperationalFailuresPage (specs/074 US3)', () => {
  const problem = (status: number, extra: Record<string, unknown> = {}) =>
    HttpResponse.json({ title: 'Conflict', status, ...extra }, { status, headers: { 'Content-Type': 'application/problem+json' } })

  it('reads its filters from the URL, so a filtered view survives a reload', async () => {
    renderPage(
      '/admin/operational-failures?range=24h&state=Resolved&severity=Critical&severity=Error&engine=Voice&kind=RateLimited&provider=OpenAI',
    )
    await screen.findByText('Chat reply')

    expect(lastListUrl?.searchParams.get('state')).toBe('Resolved')
    expect(lastListUrl?.searchParams.getAll('severity')).toEqual(['Critical', 'Error'])
    expect(lastListUrl?.searchParams.getAll('engine')).toEqual(['Voice'])
    expect(lastListUrl?.searchParams.getAll('kind')).toEqual(['RateLimited'])
    expect(lastListUrl?.searchParams.get('provider')).toBe('OpenAI')
    const from = new Date(lastListUrl!.searchParams.get('from')!).getTime()
    expect(Math.abs(Date.now() - 24 * 60 * 60 * 1000 - from)).toBeLessThan(60_000)

    const filters = screen.getByRole('group', { name: 'Incident filters' })
    expect(within(filters).getByText('Last 24 hours')).toBeInTheDocument()
    expect(within(filters).getByText('Critical, Error')).toBeInTheDocument()
    expect(within(filters).getByDisplayValue('OpenAI')).toBeInTheDocument()
  })

  it('writes a filter change to the URL and asks for the first page again', async () => {
    renderPage()
    await screen.findByText('Chat reply')

    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'State' }))
    // getByRole crashes jsdom's getComputedStyle once the menu's popover is open; find the listbox directly.
    await waitFor(() => expect(document.querySelector('[role="listbox"]')).not.toBeNull())
    fireEvent.click(within(document.querySelector<HTMLElement>('[role="listbox"]')!).getByText('Open'))

    await waitFor(() => expect(screen.getByTestId('location-search')).toHaveTextContent('?state=Open'))
    await waitFor(() => expect(lastListUrl?.searchParams.get('state')).toBe('Open'))
    expect(lastListUrl?.searchParams.get('page')).toBe('1')
  })

  it('drops an unknown filter value from a hand-edited URL instead of sending it', async () => {
    renderPage('/admin/operational-failures?severity=Catastrophic&severity=Critical&state=Sideways')
    await screen.findByText('Chat reply')

    expect(lastListUrl?.searchParams.getAll('severity')).toEqual(['Critical'])
    expect(lastListUrl?.searchParams.get('state')).toBe('Unresolved')
  })

  it('hides the triage buttons from an administrator without manage', async () => {
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:id', () => HttpResponse.json({ ...detail, canManage: false })),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))
    await screen.findByText('Replace the OpenAI credential')

    expect(screen.queryByRole('group', { name: 'Incident actions' })).not.toBeInTheDocument()
    expect(screen.queryByText('Acknowledge')).not.toBeInTheDocument()
    expect(screen.queryByText('Reopen')).not.toBeInTheDocument()
  })

  it('offers Acknowledge and Resolve on an open incident, but not Reopen', async () => {
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))
    await screen.findByText('Replace the OpenAI credential')

    const actions = screen.getByRole('group', { name: 'Incident actions' })
    expect(within(actions).getByText('Acknowledge')).toBeInTheDocument()
    expect(within(actions).getByText('Resolve')).toBeInTheDocument()
    expect(within(actions).queryByText('Reopen')).not.toBeInTheDocument()
  })

  it('offers only Reopen on a resolved incident', async () => {
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:id', () =>
        HttpResponse.json({
          ...detail,
          state: 'Resolved',
          resolved: { by: detail.sampleUsers[0], atUtc: '2026-09-22T11:00:00Z', note: 'Rotated the key' },
        }),
      ),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))
    await screen.findByText('Rotated the key')

    const actions = screen.getByRole('group', { name: 'Incident actions' })
    expect(within(actions).getByText('Reopen')).toBeInTheDocument()
    expect(within(actions).queryByText('Acknowledge')).not.toBeInTheDocument()
    expect(within(actions).queryByText('Resolve')).not.toBeInTheDocument()
  })

  it('acknowledges an incident and refreshes the detail and the nav badge', async () => {
    let acknowledged = false
    let sentBody: string | undefined
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:id', () =>
        HttpResponse.json(acknowledged ? { ...detail, state: 'Acknowledged' } : detail),
      ),
      http.post('*/api/v1/admin/operational-failures/incidents/:id/actions/acknowledge', async ({ request }) => {
        acknowledged = true
        sentBody = await request.text()
        return HttpResponse.json({
          ...detail,
          state: 'Acknowledged',
          acknowledged: { by: detail.sampleUsers[0], atUtc: '2026-09-22T11:00:00Z' },
        })
      }),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))
    await screen.findByText('Replace the OpenAI credential')
    await waitFor(() => expect(summaryCalls).toBeGreaterThan(0))
    const badgeCallsBefore = summaryCalls

    fireEvent.click(within(screen.getByRole('group', { name: 'Incident actions' })).getByText('Acknowledge'))

    expect(await screen.findByText('Incident acknowledged.')).toBeInTheDocument()
    await waitFor(() => expect(summaryCalls).toBeGreaterThan(badgeCallsBefore))
    // research D19 — no rowVersion travels with a transition.
    expect(sentBody).toBe('')
    expect(within(screen.getByRole('group', { name: 'Incident actions' })).queryByText('Acknowledge')).not.toBeInTheDocument()
  })

  it('resolves with a note, counting the characters as they are typed', async () => {
    let sentNote: unknown
    server.use(
      http.post('*/api/v1/admin/operational-failures/incidents/:id/actions/resolve', async ({ request }) => {
        sentNote = ((await request.json()) as { note: unknown }).note
        return HttpResponse.json({ ...detail, state: 'Resolved' })
      }),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))
    await screen.findByText('Replace the OpenAI credential')

    fireEvent.click(within(screen.getByRole('group', { name: 'Incident actions' })).getByText('Resolve'))
    const note = await screen.findByLabelText('Note (optional)')
    fireEvent.change(note, { target: { value: 'Rotated the key' } })
    expect(screen.getByText('15/500')).toBeInTheDocument()
    expect(note).toHaveAttribute('maxlength', '500')

    fireEvent.click(screen.getAllByText('Resolve').at(-1)!)

    expect(await screen.findByText('Incident resolved.')).toBeInTheDocument()
    expect(sentNote).toBe('Rotated the key')
  })

  it('says the incident was changed by someone else on a 409, and reloads it', async () => {
    let detailCalls = 0
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:id', () => {
        detailCalls += 1
        return HttpResponse.json(detail)
      }),
      http.post('*/api/v1/admin/operational-failures/incidents/:id/actions/acknowledge', () =>
        problem(409, { type: 'https://asklucy.app/problems/incident-conflict' }),
      ),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))
    await screen.findByText('Replace the OpenAI credential')
    const callsBefore = detailCalls

    fireEvent.click(within(screen.getByRole('group', { name: 'Incident actions' })).getByText('Acknowledge'))

    expect(await screen.findByText('This incident was changed by someone else — reloaded.')).toBeInTheDocument()
    await waitFor(() => expect(detailCalls).toBeGreaterThan(callsBefore))
  })

  it('links to the newer incident when a reopen collides with one', async () => {
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:id', ({ params }) =>
        params.id === 'incident-newer'
          ? HttpResponse.json({ ...detail, id: 'incident-newer', operation: 'Newer chat reply' })
          : HttpResponse.json({ ...detail, state: 'Resolved' }),
      ),
      http.post('*/api/v1/admin/operational-failures/incidents/:id/actions/reopen', () =>
        problem(409, { newerIncidentId: 'incident-newer' }),
      ),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))
    fireEvent.click(await screen.findByText('Reopen'))

    expect(await screen.findByText('A newer incident is already open for this cause.')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Open it'))

    expect(await screen.findByText('Newer chat reply')).toBeInTheDocument()
  })

  it('shows a transition failure other than a conflict as an error', async () => {
    server.use(
      http.post('*/api/v1/admin/operational-failures/incidents/:id/actions/acknowledge', () =>
        problem(500, { title: 'Server error', detail: 'The trail is unavailable.' }),
      ),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))
    fireEvent.click(await screen.findByText('Acknowledge'))

    expect(await screen.findByText('The trail is unavailable.')).toBeInTheDocument()
  })

  it('lists the incidents sharing a cause, and resolves them all with a summary of the outcome', async () => {
    const related: IncidentSummary = {
      ...credentialIncident,
      id: 'incident-related',
      operation: 'Document indexing',
      subject: { type: 'Document', id: 'doc-1', label: 'Site plan.pdf', deleted: false },
    }
    let resolvedKey: string | undefined
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:id', () => HttpResponse.json({ ...detail, relatedOpenCount: 39 })),
      http.get('*/api/v1/admin/operational-failures/incidents/:id/related', () =>
        HttpResponse.json({ items: [related], totalCount: 39, page: 1, pageSize: 10 }),
      ),
      http.post('*/api/v1/admin/operational-failures/root-causes/:key/actions/resolve', ({ params }) => {
        resolvedKey = String(params.key)
        return HttpResponse.json({
          attempted: 40,
          succeeded: 38,
          skipped: 1,
          failed: [{ incidentId: 'incident-stuck', reason: 'Changed by someone else' }],
        })
      }),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))

    fireEvent.click(await screen.findByText('39 other incidents share this cause'))
    expect(await screen.findByText('Site plan.pdf')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Resolve all'))
    await screen.findByLabelText('Note (optional)')
    fireEvent.click(screen.getAllByText('Resolve all').at(-1)!)

    expect(await screen.findByText('Resolved 38, skipped 1, failed 1')).toBeInTheDocument()
    expect(screen.getByText('incident-stuck')).toBeInTheDocument()
    expect(resolvedKey).toBe('root-openai')
  })

  it('shows a retry when the related incidents cannot be loaded', async () => {
    server.use(
      http.get('*/api/v1/admin/operational-failures/incidents/:id', () => HttpResponse.json({ ...detail, relatedOpenCount: 2 })),
      http.get('*/api/v1/admin/operational-failures/incidents/:id/related', () => problem(500)),
    )
    renderPage()
    fireEvent.click(await screen.findByText('Chat reply'))
    fireEvent.click(await screen.findByText('2 other incidents share this cause'))

    expect(await screen.findByText('Could not load the related incidents.')).toBeInTheDocument()
  })
})
