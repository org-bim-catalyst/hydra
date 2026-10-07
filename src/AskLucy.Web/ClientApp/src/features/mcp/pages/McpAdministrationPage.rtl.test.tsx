import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { McpServer } from '../api/mcpServersApi'
import { McpAdministrationPage } from './McpAdministrationPage'

expect.extend(toHaveNoViolations)

const mcpServer: McpServer = {
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
}

let deleteFails = false

const server = setupServer(
  http.get('*/api/v1/admin/mcp/servers', () =>
    HttpResponse.json({ items: [mcpServer], nextCursor: null }),
  ),
  http.get('*/api/v1/admin/mcp/servers/server-1', () => HttpResponse.json(mcpServer)),
  http.get('*/api/v1/admin/mcp/servers/server-1/health', () =>
    HttpResponse.json({
      mcpServerId: 'server-1',
      status: 'Degraded',
      failureCategory: null,
      detail: 'Slow responses from the server.',
      checkedAtUtc: '2026-08-02T00:00:00Z',
      consecutiveFailureCount: 0,
    }),
  ),
  http.get('*/api/v1/admin/mcp/servers/server-1/tools', () =>
    HttpResponse.json([
      {
        id: 'tool-1',
        mcpServerId: 'server-1',
        namespacedName: 'acme.search',
        toolName: 'search',
        displayName: 'Search docs',
        description: 'Searches the documentation.',
        inputSchemaJson: '{}',
        outputSchemaJson: '{}',
        serverDeclaredRiskLevel: null,
        effectiveRiskLevel: 'High',
        requiredPermissions: ['docs.read'],
        activationStatus: 'PendingReview',
        activatedByUserId: null,
        activatedAtUtc: null,
        version: null,
        isAvailable: true,
      },
    ]),
  ),
  http.get('*/api/v1/admin/mcp/servers/server-1/audit-log', () =>
    HttpResponse.json({
      items: [
        {
          id: 'audit-1',
          mcpServerId: 'server-1',
          userId: 'admin-1',
          action: 'ServerRegistered',
          failureCategory: null,
          detailsJson: '{"name":"Acme Docs"}',
          occurredAtUtc: '2026-08-01T10:30:00Z',
        },
      ],
      nextCursor: null,
    }),
  ),
  http.delete('*/api/v1/admin/mcp/servers/server-1', () =>
    deleteFails
      ? HttpResponse.json(
          { title: 'الخادم مرتبط بأداة وكيل.', detail: 'الخادم مرتبط بأداة وكيل.' },
          { status: 409 },
        )
      : new HttpResponse(null, { status: 204 }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  deleteFails = false
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <LocalizedSurface scope="page">
          <McpAdministrationPage />
        </LocalizedSurface>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('McpAdministrationPage in Arabic (T217)', () => {
  it('renders the registry in ar/rtl with Arabic copy and protected terms intact', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    // "MCP" is a protected term: it stays in Latin script inside the Arabic title.
    expect(await screen.findByText('خوادم MCP')).toBeInTheDocument()
    // The admin route's page surface sets the document language and direction while it is mounted.
    expect(document.documentElement).toHaveAttribute('lang', 'ar')
    expect(document.documentElement).toHaveAttribute('dir', 'rtl')
    expect(await screen.findByText('Acme Docs')).toBeInTheDocument()
    expect(screen.getByText('https://mcp.acme.example.com')).toBeInTheDocument()
    expect(screen.getByText('HTTP قابل للبث')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'تسجيل خادم' })).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'اختبار الاتصال بـ ⁨Acme Docs⁩' }),
    ).toBeInTheDocument()
    expect(screen.getByRole('switch', { name: 'تعطيل ⁨Acme Docs⁩' })).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the selected server with translated health, tools and audit log', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    fireEvent.click(await screen.findByText('Acme Docs'))

    expect(await screen.findByText('متدهور')).toBeInTheDocument()
    expect(await screen.findByText('الأدوات')).toBeInTheDocument()
    expect(await screen.findByText('Search docs')).toBeInTheDocument()
    expect(screen.getByText('عالية')).toBeInTheDocument()
    expect(screen.getByText('بانتظار المراجعة')).toBeInTheDocument()
    expect(screen.getByText('docs.read')).toBeInTheDocument()
    expect(screen.getByText('سجل التدقيق')).toBeInTheDocument()
    expect(await screen.findByText('ServerRegistered')).toBeInTheDocument()
    expect(screen.getByText('{"name":"Acme Docs"}')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'تنشيط ⁨Search docs⁩' })).toBeInTheDocument()
    // Dates are Gregorian with Western digits.
    expect(screen.getByText(/2026/)).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('opens the register dialog in Arabic, rtl', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'تسجيل خادم' }))

    // Open MUI dialogs break getByRole in jsdom, so plain text queries.
    expect(await screen.findByText('تسجيل خادم MCP')).toBeInTheDocument()
    expect(screen.getAllByText('نوع المصادقة').length).toBeGreaterThan(0)
    expect(screen.getByText('السماح بنقل غير آمن (بدون TLS)')).toBeInTheDocument()
    expect(document.querySelector('.MuiDialog-root')).toHaveAttribute('dir', 'rtl')
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('surfaces a failed delete as the server message', async () => {
    deleteFails = true
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'حذف ⁨Acme Docs⁩' }))

    await waitFor(() => expect(screen.getByText('الخادم مرتبط بأداة وكيل.')).toBeInTheDocument())
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage()
      await screen.findByText('Acme Docs')
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})
