import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import type { AdminAiTool, ToolSettings } from '../api/adminAiToolsApi'
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

/** The settings the server declares for the one configurable tool. */
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

/** The caller's permissions; changed per test to cover the view-only case. */
let permissions: string[] = ['admin.ai-tools.view', 'admin.ai-tools.manage']
/** What the last save sent, captured from the PUT. */
let savedValues: Record<string, string> | null = null

const server = setupServer(
  // AdminShell's permission-gated nav reads the session; without this the request escapes to the
  // real network (an MSW "unhandled request" warning in CI).
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions }),
  ),
  http.get('*/api/v1/admin/ai-tools', () => HttpResponse.json(tools)),
  http.get('*/api/v1/admin/ai-tools/settings', () => HttpResponse.json(toolSettings)),
  http.put('*/api/v1/admin/ai-tools/map_snapshot/settings', async ({ request }) => {
    const body = (await request.json()) as { values: Record<string, string> }
    savedValues = body.values
    return new HttpResponse(null, { status: 204 })
  }),
)

beforeAll(() => server.listen())
beforeEach(() => {
  permissions = ['admin.ai-tools.view', 'admin.ai-tools.manage']
  savedValues = null
})
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

  it('keeps the catalogue itself read-only — the only per-tool action is that tool’s settings gear', async () => {
    renderPage()

    await screen.findByText('map_snapshot')
    expect(screen.queryByRole('button', { name: /edit|delete|disable|enable/i })).not.toBeInTheDocument()
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

describe('AdminAiToolsPage settings column', () => {
  it('gives every tool a gear, enabled only where the tool declares settings', async () => {
    renderPage()

    await screen.findByText('map_snapshot')
    expect(screen.getByRole('columnheader', { name: 'Settings' })).toBeInTheDocument()

    const configurable = screen.getByRole('button', { name: 'Settings for map_snapshot' })
    const notConfigurable = screen.getByRole('button', { name: 'Settings for mcp:atlassian:search' })
    await waitFor(() => expect(configurable).toBeEnabled())
    expect(notConfigurable).toBeDisabled()
  })

  it('opens the tool’s own settings, showing the server’s labels and descriptions', async () => {
    renderPage()

    await screen.findByText('map_snapshot')
    fireEvent.click(screen.getByRole('button', { name: 'Settings for map_snapshot' }))

    expect(await screen.findByText('map_snapshot settings')).toBeInTheDocument()
    expect(screen.getByText('Default imagery provider')).toBeInTheDocument()
    expect(screen.getByText('Esri World Imagery')).toBeInTheDocument()
    // The label of the number field is found through the input it labels.
    expect(screen.getByLabelText('Default crop')).toBeInTheDocument()
    expect(screen.getByText('How many pixels are cropped from each of the four edges.')).toBeInTheDocument()
    expect(screen.getByText("Correct Google's colour")).toBeInTheDocument()
    expect(screen.getByText("Google tone-maps its satellite layer for the web.")).toBeInTheDocument()
  })

  it('saves the changed values and says so', async () => {
    renderPage()

    await screen.findByText('map_snapshot')
    fireEvent.click(screen.getByRole('button', { name: 'Settings for map_snapshot' }))
    await screen.findByText('map_snapshot settings')

    fireEvent.change(screen.getByLabelText('Default crop'), { target: { value: '64' } })
    fireEvent.click(screen.getByLabelText("Correct Google's colour"))

    fireEvent.click(screen.getByText('Save'))

    await waitFor(() =>
      expect(savedValues).toEqual({
        defaultSource: 'esri',
        defaultCropPixels: '64',
        enhanceGoogleColour: 'false',
      }),
    )
    expect(await screen.findByText('Tool settings saved.')).toBeInTheDocument()
  })

  it('keeps the dialog open and says why when the save is refused', async () => {
    server.use(
      http.put('*/api/v1/admin/ai-tools/map_snapshot/settings', () =>
        HttpResponse.json({ detail: 'The crop must be 200 pixels or less.' }, { status: 400 }),
      ),
    )
    renderPage()

    await screen.findByText('map_snapshot')
    fireEvent.click(screen.getByRole('button', { name: 'Settings for map_snapshot' }))
    await screen.findByText('map_snapshot settings')

    fireEvent.change(screen.getByLabelText('Default crop'), { target: { value: '64' } })
    fireEvent.click(screen.getByText('Save'))

    expect(await screen.findByText('The crop must be 200 pixels or less.')).toBeInTheDocument()
    expect(screen.getByText('The settings could not be saved. Please try again.')).toBeInTheDocument()
    // The dialog stays open so the value can be corrected and the save retried.
    expect(screen.getByText('map_snapshot settings')).toBeInTheDocument()
  })

  it('shows the settings to a caller who may view but not change them', async () => {
    permissions = ['admin.ai-tools.view']
    renderPage()

    await screen.findByText('map_snapshot')
    fireEvent.click(screen.getByRole('button', { name: 'Settings for map_snapshot' }))

    expect(await screen.findByText('map_snapshot settings')).toBeInTheDocument()
    expect(
      screen.getByText(
        'You can see these settings but not change them. Changing them needs the Manage AI tools permission.',
      ),
    ).toBeInTheDocument()
    expect(screen.getByText('Save').closest('button')).toBeDisabled()
  })

  it('keeps every gear disabled and says why when the settings cannot be loaded', async () => {
    server.use(
      http.get('*/api/v1/admin/ai-tools/settings', () =>
        HttpResponse.json({ detail: 'The tool settings could not be read.' }, { status: 500 }),
      ),
    )
    renderPage()

    expect(
      await screen.findByText('Could not load the tool settings, so every settings button is disabled.'),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Settings for map_snapshot' })).toBeDisabled(),
    )
  })
})
