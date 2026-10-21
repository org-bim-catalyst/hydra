import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { configure, render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import {
  lacksArabicIndicDigits,
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { AdminAiTool } from '../api/adminAiToolsApi'
import { AdminAiToolsPage } from './AdminAiToolsPage'

expect.extend(toHaveNoViolations)
// The first render pulls in the whole admin shell, which is slow when the suite runs in parallel.
vi.setConfig({ testTimeout: 20000 })
configure({ asyncUtilTimeout: 8000 })

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
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions: [] }),
  ),
  http.get('*/api/v1/admin/ai-tools', () => HttpResponse.json(tools)),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <LocalizedSurface scope="subtree">
          <AdminAiToolsPage />
        </LocalizedSurface>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminAiToolsPage in Arabic', () => {
  it('renders in ar/rtl with Arabic headers and risk levels, and keeps identifiers LTR', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderPage()

    expect(await screen.findByText('map_snapshot')).toBeInTheDocument()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('columnheader', { name: 'الأداة' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'المصدر' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'الخطورة' })).toBeInTheDocument()
    expect(screen.getByText('منخفضة')).toBeInTheDocument()
    expect(screen.getByText('حرجة')).toBeInTheDocument()
    // The protected term stays Latin inside the Arabic phrase.
    expect(screen.getByText('MCP')).toBeInTheDocument()
    // Identifiers keep their direction.
    expect(screen.getByText('map_snapshot')).toHaveAttribute('dir', 'ltr')
    for (const bdi of container.querySelectorAll('tbody bdi')) {
      expect(lacksArabicIndicDigits(bdi.textContent ?? '')).toBe(true)
    }
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows an Arabic empty state', async () => {
    server.use(http.get('*/api/v1/admin/ai-tools', () => HttpResponse.json([])))
    renderPage()

    expect(await screen.findByText('لا توجد أدوات مسجَّلة حاليًا.')).toBeInTheDocument()
  })

  it('shows the server detail, or an Arabic message, with a retry when loading fails', async () => {
    server.use(http.get('*/api/v1/admin/ai-tools', () => HttpResponse.error()))
    renderPage()

    expect(await screen.findByText('تعذّر تحميل أدوات الذكاء الاصطناعي.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    const { container } = renderPage()
    await screen.findByText('map_snapshot')

    expect(await axe(container)).toHaveNoViolations()
  })
})
