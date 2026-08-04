import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { KnowledgeBaseCategory } from '../api/knowledgeBaseTaxonomyApi'
import { KnowledgeBaseEditDialog } from './KnowledgeBaseEditDialog'

expect.extend(toHaveNoViolations)

const categories: KnowledgeBaseCategory[] = [
  { id: 'cat-1', name: 'Engineering', isPredefined: true },
  { id: 'cat-2', name: 'Vendor Docs', isPredefined: false },
]

const server = setupServer(http.get('*/api/v1/knowledge-bases/categories', () => HttpResponse.json(categories)))

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('KnowledgeBaseEditDialog accessibility (FR-039–FR-042, SC-010)', () => {
  it('has no automatically detectable a11y violations when creating a knowledge base', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { baseElement, findByLabelText } = render(
      <QueryClientProvider client={queryClient}>
        <KnowledgeBaseEditDialog open submitting={false} errorMessage={null} onSubmit={vi.fn()} onClose={vi.fn()} />
      </QueryClientProvider>,
    )

    await findByLabelText('Description')

    const results = await axe(baseElement)
    expect(results).toHaveNoViolations()
  })
})
