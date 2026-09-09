import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AdminSystemAgent } from '../api/adminSystemAgentsApi'
import { AdminSystemAgentsPage } from './AdminSystemAgentsPage'

expect.extend(toHaveNoViolations)

const agents: AdminSystemAgent[] = [
  {
    id: 'agent-1',
    name: 'Lucy',
    systemKey: 'lucy.orchestrator',
    status: 'Published',
    publishedVersionNumber: 2,
    lastUpdatedAtUtc: '2026-09-01T12:34:56Z',
  },
  {
    id: 'agent-2',
    name: 'Site Analyst',
    systemKey: 'lucy.site',
    status: 'Published',
    publishedVersionNumber: 1,
    lastUpdatedAtUtc: '2026-09-01T12:00:00Z',
  },
]

const server = setupServer(http.get('*/api/v1/admin/agents/system', () => HttpResponse.json(agents)))

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminSystemAgentsPage accessibility', () => {
  it('has no automatically detectable a11y violations with agents listed (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminSystemAgentsPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('Lucy')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in the empty state (constitution §10)', async () => {
    server.use(http.get('*/api/v1/admin/agents/system', () => HttpResponse.json([])))
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminSystemAgentsPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('No system agents are currently provisioned.')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
