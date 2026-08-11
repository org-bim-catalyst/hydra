import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { McpServer } from '../api/mcpServersApi'
import { McpAdministrationPage } from './McpAdministrationPage'

expect.extend(toHaveNoViolations)

const servers: McpServer[] = [
  {
    id: 'server-1',
    name: 'Acme Docs',
    description: 'Internal documentation server.',
    endpoint: 'https://mcp.acme.example.com',
    transport: 'StreamableHttp',
    authenticationType: 'ApiKey',
    requiresUnauthenticatedConfirmation: false,
    allowInsecureTransport: false,
    insecureTransportJustification: null,
    endpointValidationOverride: false,
    endpointValidationJustification: null,
    isEnabled: true,
    ownerUserId: 'admin-1',
    configurationVersion: 1,
    capabilityRefreshIntervalMinutes: 60,
    lastHealthCheckAtUtc: null,
    lastCapabilityDiscoveryAtUtc: null,
    createdAtUtc: '2026-08-01T00:00:00Z',
    modifiedAtUtc: null,
  },
]

const server = setupServer(
  http.get('*/api/v1/admin/mcp/servers*', () => HttpResponse.json({ items: servers, nextCursor: null })),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('McpAdministrationPage accessibility (specs/021-mcp-integration User Story 1)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <McpAdministrationPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('Acme Docs')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
