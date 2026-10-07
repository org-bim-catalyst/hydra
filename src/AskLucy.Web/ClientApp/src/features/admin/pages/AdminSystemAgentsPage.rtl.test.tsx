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
import type { AdminSystemAgent } from '../api/adminSystemAgentsApi'
import { AdminSystemAgentsPage } from './AdminSystemAgentsPage'

expect.extend(toHaveNoViolations)
// The first render pulls in the whole admin shell, which is slow when the suite runs in parallel.
vi.setConfig({ testTimeout: 20000 })
configure({ asyncUtilTimeout: 8000 })

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
    name: 'Drafty',
    systemKey: null,
    status: 'Draft',
    publishedVersionNumber: null,
    lastUpdatedAtUtc: '2026-09-02T08:00:00Z',
  },
]

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions: [] }),
  ),
  http.get('*/api/v1/admin/agents/system', () => HttpResponse.json(agents)),
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
          <AdminSystemAgentsPage />
        </LocalizedSurface>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminSystemAgentsPage in Arabic (T214)', () => {
  it('renders in ar/rtl with Arabic headers, statuses and Western-digit dates', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderPage()

    expect(await screen.findByText('Lucy')).toBeInTheDocument()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('columnheader', { name: 'مفتاح النظام' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'آخر تحديث' })).toBeInTheDocument()
    expect(screen.getByText('منشور')).toBeInTheDocument()
    expect(screen.getByText('مسودة')).toBeInTheDocument()
    // The protected term stays Latin inside the Arabic phrase.
    expect(screen.getAllByText('مُهيَّأ بواسطة Ask Lucy')).toHaveLength(2)
    // Identifiers keep their direction and the version is a Western digit.
    expect(screen.getByText('lucy.orchestrator')).toHaveAttribute('dir', 'ltr')
    expect(screen.getByText('2')).toHaveAttribute('dir', 'ltr')
    for (const bdi of container.querySelectorAll('tbody bdi')) {
      expect(lacksArabicIndicDigits(bdi.textContent ?? '')).toBe(true)
    }
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows an Arabic empty state', async () => {
    server.use(http.get('*/api/v1/admin/agents/system', () => HttpResponse.json([])))
    renderPage()

    expect(await screen.findByText('لا يوجد وكلاء نظام مُهيَّؤون حاليًا.')).toBeInTheDocument()
  })

  it('shows the server detail, or an Arabic message, with a retry when loading fails', async () => {
    server.use(http.get('*/api/v1/admin/agents/system', () => HttpResponse.error()))
    renderPage()

    expect(await screen.findByText('تعذّر تحميل وكلاء النظام.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    const { container } = renderPage()
    await screen.findByText('Lucy')

    expect(await axe(container)).toHaveNoViolations()
  })
})
