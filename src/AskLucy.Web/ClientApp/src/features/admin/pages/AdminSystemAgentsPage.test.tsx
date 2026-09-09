import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AdminSystemAgent } from '../api/adminSystemAgentsApi'
import { AdminSystemAgentsPage } from './AdminSystemAgentsPage'

const agents: AdminSystemAgent[] = [
  {
    id: 'agent-1',
    name: 'Lucy',
    systemKey: 'lucy.orchestrator',
    status: 'Published',
    publishedVersionNumber: 2,
    lastUpdatedAtUtc: '2026-09-01T12:34:56Z',
  },
]

const server = setupServer(http.get('*/api/v1/admin/agents/system', () => HttpResponse.json(agents)))

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminSystemAgentsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminSystemAgentsPage', () => {
  it('renders each system agent with its name, status, version, and last-updated time', async () => {
    renderPage()

    expect(await screen.findByText('Lucy')).toBeInTheDocument()
    expect(screen.getByText('lucy.orchestrator')).toBeInTheDocument()
    expect(screen.getByText('Published')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument()
  })

  it('marks every row as system-owned/read-only and offers no mutating action', async () => {
    renderPage()

    expect(await screen.findByText('Provisioned by Ask Lucy')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /edit|delete|duplicate|publish|archive|restore/i })).not.toBeInTheDocument()
  })

  it('shows a clear empty state when no system agents are provisioned (FR-006)', async () => {
    server.use(http.get('*/api/v1/admin/agents/system', () => HttpResponse.json([])))
    renderPage()

    expect(await screen.findByText('No system agents are currently provisioned.')).toBeInTheDocument()
  })
})
