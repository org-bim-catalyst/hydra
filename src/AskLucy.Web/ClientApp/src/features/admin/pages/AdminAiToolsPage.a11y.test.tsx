import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AdminAiTool } from '../api/adminAiToolsApi'
import { AdminAiToolsPage } from './AdminAiToolsPage'

expect.extend(toHaveNoViolations)

const tools: AdminAiTool[] = [
  {
    name: 'map_snapshot',
    description: 'Captures the current satellite imagery of a place and returns it as an image in the chat.',
    riskLevel: 'Low',
    requiredPermissions: ['ExternalNetwork', 'WriteFile'],
    inputSchemaJson: '{"type":"object"}',
    outputSchemaJson: '{"type":"object"}',
  },
  {
    name: 'mcp:atlassian:search',
    description: 'Searches Atlassian (MCP server: Atlassian)',
    riskLevel: 'Critical',
    requiredPermissions: ['ReadExternalData'],
    inputSchemaJson: '{"type":"object"}',
    outputSchemaJson: '{"type":"object"}',
  },
]

const server = setupServer(
  // AdminShell's permission-gated nav reads the session; without this the request escapes to the
  // real network (an MSW "unhandled request" warning in CI).
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions: [] }),
  ),
  http.get('*/api/v1/admin/ai-tools', () => HttpResponse.json(tools)),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminAiToolsPage accessibility', () => {
  it('has no automatically detectable a11y violations with tools listed (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminAiToolsPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('map_snapshot')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in the empty state (constitution §10)', async () => {
    server.use(http.get('*/api/v1/admin/ai-tools', () => HttpResponse.json([])))
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminAiToolsPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('No tools are currently registered.')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
