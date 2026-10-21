import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AdminAiTool } from '../api/adminAiToolsApi'
import { AdminAiToolsPage } from './AdminAiToolsPage'

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
    riskLevel: 'High',
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

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminAiToolsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminAiToolsPage', () => {
  it('renders each tool with its name, description, risk level, and required permissions', async () => {
    renderPage()

    expect(await screen.findByText('map_snapshot')).toBeInTheDocument()
    expect(
      screen.getByText('Captures the current satellite imagery of a place and returns it as an image in the chat.'),
    ).toBeInTheDocument()
    expect(screen.getByText('Low')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
    expect(screen.getByText('ExternalNetwork, WriteFile')).toBeInTheDocument()
  })

  it('distinguishes a built-in tool from an MCP one by its namespaced name', async () => {
    renderPage()

    expect(await screen.findByText('map_snapshot')).toBeInTheDocument()
    expect(screen.getByText('Built-in')).toBeInTheDocument()
    expect(screen.getByText('MCP')).toBeInTheDocument()
  })

  it('offers no mutating action — the catalogue is a fact about the deployment, not a setting', async () => {
    renderPage()

    await screen.findByText('map_snapshot')
    expect(screen.queryByRole('button', { name: /edit|delete|disable|enable|configure/i })).not.toBeInTheDocument()
  })

  it('shows a clear empty state when no tools are registered', async () => {
    server.use(http.get('*/api/v1/admin/ai-tools', () => HttpResponse.json([])))
    renderPage()

    expect(await screen.findByText('No tools are currently registered.')).toBeInTheDocument()
  })

  it('surfaces a load failure with a retry affordance instead of an empty table', async () => {
    server.use(
      http.get('*/api/v1/admin/ai-tools', () =>
        HttpResponse.json({ detail: 'The tool catalogue could not be read.' }, { status: 500 }),
      ),
    )
    renderPage()

    expect(await screen.findByText('The tool catalogue could not be read.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
  })
})
