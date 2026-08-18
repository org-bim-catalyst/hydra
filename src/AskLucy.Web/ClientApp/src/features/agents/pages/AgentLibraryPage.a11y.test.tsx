import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AgentListItem } from '../api/agentsApi'
import { AgentLibraryPage } from './AgentLibraryPage'

expect.extend(toHaveNoViolations)

const agents: AgentListItem[] = [
  {
    id: 'agent-1',
    name: 'Research Assistant',
    description: 'Searches the knowledge base and summarizes findings.',
    agentType: 'Research',
    status: 'Published',
    publishedVersionNumber: 1,
    createdAtUtc: '2026-08-01T00:00:00Z',
    modifiedAtUtc: null,
  },
  {
    id: 'agent-2',
    name: 'Draft Agent',
    description: null,
    agentType: 'Task',
    status: 'Draft',
    publishedVersionNumber: null,
    createdAtUtc: '2026-08-02T00:00:00Z',
    modifiedAtUtc: null,
  },
]

const server = setupServer(http.get('*/api/v1/agents', () => HttpResponse.json({ items: agents, nextCursor: null })))

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AgentLibraryPage accessibility (spec.md User Story 1/User Story 6)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AgentLibraryPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('Research Assistant')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
