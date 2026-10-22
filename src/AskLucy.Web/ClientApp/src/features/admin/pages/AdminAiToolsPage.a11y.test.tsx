import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AdminAiTool, ToolSettings } from '../api/adminAiToolsApi'
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

const toolSettings: ToolSettings[] = [
  {
    tool: 'map_snapshot',
    settings: [
      {
        key: 'defaultSource',
        valueType: 'Choice',
        label: 'Default imagery provider',
        description: 'Which provider a snapshot uses when the request does not name one.',
        value: 'esri',
        defaultValue: 'esri',
        options: [
          { value: 'esri', label: 'Esri World Imagery' },
          { value: 'google', label: 'Google Static Maps' },
        ],
        minimum: null,
        maximum: null,
      },
      {
        key: 'defaultCropPixels',
        valueType: 'Integer',
        label: 'Default crop',
        description: 'How many pixels are cropped from each of the four edges.',
        value: '32',
        defaultValue: '32',
        options: [],
        minimum: 0,
        maximum: 200,
      },
      {
        key: 'enhanceGoogleColour',
        valueType: 'Boolean',
        label: "Correct Google's colour",
        description: "Google tone-maps its satellite layer for the web.",
        value: 'true',
        defaultValue: 'true',
        options: [],
        minimum: null,
        maximum: null,
      },
    ],
  },
]

const server = setupServer(
  // AdminShell's permission-gated nav reads the session; without this the request escapes to the
  // real network (an MSW "unhandled request" warning in CI).
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: [],
      permissions: ['admin.ai-tools.view', 'admin.ai-tools.manage'],
    }),
  ),
  http.get('*/api/v1/admin/ai-tools', () => HttpResponse.json(tools)),
  http.get('*/api/v1/admin/ai-tools/settings', () => HttpResponse.json(toolSettings)),
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

  it('has no automatically detectable a11y violations with the settings dialog open (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { findByText, findByRole } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminAiToolsPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('map_snapshot')
    fireEvent.click(await findByRole('button', { name: 'Settings for map_snapshot' }))
    await findByText('map_snapshot settings')

    // The dialog renders in a portal, so axe runs over the whole document rather than the
    // container.
    const results = await axe(document.body)
    expect(results).toHaveNoViolations()
  })
})
