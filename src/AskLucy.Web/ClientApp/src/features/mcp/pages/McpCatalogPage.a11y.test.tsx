import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { McpToolCatalogSummary } from '../api/mcpCatalogApi'
import { McpCatalogPage } from './McpCatalogPage'

expect.extend(toHaveNoViolations)

const tools: McpToolCatalogSummary[] = [
  {
    namespacedName: 'mcp:11111111-1111-1111-1111-111111111111:search',
    displayName: 'Search',
    description: 'Searches internal documents.',
    sourceServerName: 'Acme Docs',
    effectiveRiskLevel: 'Low',
    requiredPermissions: ['ReadExternalData'],
  },
]

const server = setupServer(
  http.get('*/api/v1/mcp/catalog/tools', () => HttpResponse.json(tools)),
  http.get('*/api/v1/mcp/catalog/resources', () => HttpResponse.json([])),
  http.get('*/api/v1/mcp/catalog/prompts', () => HttpResponse.json([])),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('McpCatalogPage accessibility (specs/021-mcp-integration User Stories 4-5)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <McpCatalogPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('Search')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
