import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type * as agentPoliciesApiModule from '../api/agentPoliciesApi'
import * as agentPoliciesApi from '../api/agentPoliciesApi'
import type { AgentPolicy } from '../api/agentPoliciesApi'
import { AgentPoliciesAdminPage } from './AgentPoliciesAdminPage'

expect.extend(toHaveNoViolations)

vi.mock('../api/agentPoliciesApi', async () => {
  const actual = await vi.importActual<typeof agentPoliciesApiModule>('../api/agentPoliciesApi')
  return {
    ...actual,
    listAgentPolicies: vi.fn(),
    createAgentPolicy: vi.fn(),
    updateAgentPolicy: vi.fn(),
    deleteAgentPolicy: vi.fn(),
  }
})

const policy: AgentPolicy = {
  id: 'policy-1',
  name: 'Read-only fake tool',
  description: null,
  toolName: 'FakeHighRiskTool',
  conditionsJson: null,
  createdByUserId: 'user-1',
  isEnabled: true,
  createdAtUtc: '2026-07-20T00:00:00Z',
  modifiedAtUtc: null,
}

beforeEach(() => {
  vi.clearAllMocks()
  setTheme()
})
afterEach(() => signOutAndResetTheme())

function renderPage(policies: AgentPolicy[] = [policy]) {
  vi.mocked(agentPoliciesApi.listAgentPolicies).mockResolvedValue(policies)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <LocalizedSurface scope="page">
          <AgentPoliciesAdminPage />
        </LocalizedSurface>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AgentPoliciesAdminPage in Arabic (T216)', () => {
  it('renders in ar/rtl with Arabic copy, data as returned, and no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    expect(await screen.findByText('سياسات الوكلاء')).toBeInTheDocument()
    // The admin route's page surface sets the document language and direction while it is mounted.
    expect(document.documentElement).toHaveAttribute('lang', 'ar')
    expect(document.documentElement).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText(/اعتمد مسبقًا إجراءات محددة/)).toBeInTheDocument()
    expect(await screen.findByText('Read-only fake tool')).toBeInTheDocument()
    expect(screen.getByText('FakeHighRiskTool')).toBeInTheDocument()
    expect(screen.getByText('الأداة')).toBeInTheDocument()
    expect(screen.getByText('دائمًا')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'سياسة جديدة' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'حذف ⁨Read-only fake tool⁩' })).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('says in Arabic when there are no policies', async () => {
    renderPage([])
    expect(await screen.findByText('لم يتم إعداد أي سياسات بعد.')).toBeInTheDocument()
  })

  it('opens the new-policy dialog in Arabic, rtl, with the JSON example kept verbatim', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'سياسة جديدة' }))

    // Open MUI dialogs break getByRole in jsdom, so plain text queries.
    expect(await screen.findByText('اسم الأداة')).toBeInTheDocument()
    expect(screen.getByText('إنشاء السياسة')).toBeInTheDocument()
    expect(screen.getByText('إلغاء')).toBeInTheDocument()
    expect(screen.getByText(/\{"action":"read-only"\}/)).toBeInTheDocument()
    expect(document.querySelector('.MuiDialog-root')).toHaveAttribute('dir', 'rtl')
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the localized fallback when a failed mutation carries no message', async () => {
    vi.mocked(agentPoliciesApi.deleteAgentPolicy).mockRejectedValue('boom')
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'حذف ⁨Read-only fake tool⁩' }))

    await waitFor(() =>
      expect(screen.getByText('تعذّر حذف السياسة. يُرجى المحاولة مرة أخرى.')).toBeInTheDocument(),
    )
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage()
      await screen.findByText('Read-only fake tool')
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})
