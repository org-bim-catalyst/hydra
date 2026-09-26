import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
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
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminOperationalFailuresPage />
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
