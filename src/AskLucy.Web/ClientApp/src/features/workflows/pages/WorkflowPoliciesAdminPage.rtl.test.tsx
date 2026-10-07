import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
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
import type * as workflowPoliciesApiModule from '../api/workflowPoliciesApi'
import * as workflowPoliciesApi from '../api/workflowPoliciesApi'
import type { WorkflowPolicy } from '../api/workflowPoliciesApi'
import { WorkflowPoliciesAdminPage } from './WorkflowPoliciesAdminPage'

expect.extend(toHaveNoViolations)

vi.mock('../api/workflowPoliciesApi', async () => {
  const actual = await vi.importActual<typeof workflowPoliciesApiModule>(
    '../api/workflowPoliciesApi',
  )
  return {
    ...actual,
    listWorkflowPolicies: vi.fn(),
    createWorkflowPolicy: vi.fn(),
    updateWorkflowPolicy: vi.fn(),
    deleteWorkflowPolicy: vi.fn(),
  }
})

const policy: WorkflowPolicy = {
  id: 'policy-1',
  name: 'Approve knowledge search',
  description: null,
  workflowNodeType: 'RagSearch',
  underlyingToolName: 'KnowledgeSearchTool',
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

function renderPage(policies: WorkflowPolicy[] = [policy]) {
  vi.mocked(workflowPoliciesApi.listWorkflowPolicies).mockResolvedValue(policies)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <LocalizedSurface scope="page">
          <WorkflowPoliciesAdminPage />
        </LocalizedSurface>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('WorkflowPoliciesAdminPage in Arabic (T216)', () => {
  it('renders in ar/rtl with Arabic copy, a translated node type, and no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    expect(await screen.findByText('سياسات سير العمل')).toBeInTheDocument()
    // The admin route's page surface sets the document language and direction while it is mounted.
    expect(document.documentElement).toHaveAttribute('lang', 'ar')
    expect(document.documentElement).toHaveAttribute('dir', 'rtl')
    expect(await screen.findByText('Approve knowledge search')).toBeInTheDocument()
    expect(screen.getByText('نوع العقدة')).toBeInTheDocument()
    expect(screen.getByText('الأداة الأساسية')).toBeInTheDocument()
    // The node type is labelled; the tool name is an identifier shown as returned.
    expect(screen.getByText('بحث RAG')).toBeInTheDocument()
    expect(screen.getByText('KnowledgeSearchTool')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('opens the dialog in Arabic with translated node types', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage([])
    fireEvent.click(await screen.findByRole('button', { name: 'سياسة جديدة' }))

    expect((await screen.findAllByText('نوع العقدة (اختياري)')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('اسم الأداة الأساسية (اختياري)').length).toBeGreaterThan(0)
    expect(screen.getByText('إنشاء السياسة')).toBeInTheDocument()
    expect(document.querySelector('.MuiDialog-root')).toHaveAttribute('dir', 'rtl')
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage()
      await screen.findByText('Approve knowledge search')
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})
