import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { KnowledgeBaseDashboardSummary, KnowledgeBaseSummary, PagedResult } from '../api/knowledgeBasesApi'
import type { KnowledgeBaseCategory } from '../api/knowledgeBaseTaxonomyApi'
import { KnowledgeBaseDashboardPage } from './KnowledgeBaseDashboardPage'

expect.extend(toHaveNoViolations)

const knowledgeBases: KnowledgeBaseSummary[] = [
  {
    id: '11111111-1111-1111-1111-111111111111',
    name: 'BIM Standards',
    description: 'Company-wide modeling standards.',
    status: 'Active',
    color: '#4F46E5',
    icon: null,
    categoryId: null,
    tags: ['revit', 'standards'],
    isFavorite: true,
    isPinned: false,
    documentCount: 12,
    totalPageCount: 340,
    storageSizeBytes: 2_097_152,
    createdAtUtc: '2026-07-01T00:00:00Z',
    lastUpdatedAtUtc: '2026-08-01T00:00:00Z',
    isDeleted: false,
  },
]

const summary: KnowledgeBaseDashboardSummary = {
  totalKnowledgeBases: 1,
  totalDocuments: 12,
  totalStorageBytes: 2_097_152,
  recentCount: 1,
  favoritesCount: 1,
  pinnedCount: 0,
  archivedCount: 0,
}

const categories: KnowledgeBaseCategory[] = [{ id: 'cat-1', name: 'Engineering', isPredefined: true }]

const server = setupServer(
  http.get('*/api/v1/knowledge-bases', () => HttpResponse.json<PagedResult<KnowledgeBaseSummary>>({ items: knowledgeBases, nextCursor: null })),
  http.get('*/api/v1/knowledge-bases/dashboard-summary', () => HttpResponse.json(summary)),
  http.get('*/api/v1/knowledge-bases/categories', () => HttpResponse.json(categories)),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('KnowledgeBaseDashboardPage accessibility (FR-039–FR-042, SC-010)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <KnowledgeBaseDashboardPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('BIM Standards')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
