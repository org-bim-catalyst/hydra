import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { KnowledgeBaseDetail, PagedResult } from '../api/knowledgeBasesApi'
import type { FolderTree, KnowledgeBaseDocument } from '../api/knowledgeBaseFoldersApi'
import { KnowledgeBaseDetailPage } from './KnowledgeBaseDetailPage'

expect.extend(toHaveNoViolations)

const KNOWLEDGE_BASE_ID = '11111111-1111-1111-1111-111111111111'
const ROOT_FOLDER_ID = '22222222-2222-2222-2222-222222222222'

const knowledgeBase: KnowledgeBaseDetail = {
  id: KNOWLEDGE_BASE_ID,
  ownerId: 'user-1',
  name: 'BIM Standards',
  description: 'Company-wide modeling standards.',
  status: 'Active',
  color: '#4F46E5',
  icon: null,
  categoryId: null,
  tags: [],
  notes: null,
  isFavorite: false,
  isPinned: false,
  documentCount: 1,
  totalPageCount: 10,
  storageSizeBytes: 1024,
  createdAtUtc: '2026-07-01T00:00:00Z',
  lastUpdatedAtUtc: '2026-08-01T00:00:00Z',
  isDeleted: false,
}

const folderTree: FolderTree = {
  folders: [{ id: ROOT_FOLDER_ID, knowledgeBaseId: KNOWLEDGE_BASE_ID, parentFolderId: null, name: 'Drawings', depth: 0 }],
  rootDocuments: [],
}

const folderDocuments: KnowledgeBaseDocument[] = [
  {
    id: '33333333-3333-3333-3333-333333333333',
    knowledgeBaseId: KNOWLEDGE_BASE_ID,
    folderId: ROOT_FOLDER_ID,
    fileName: 'standards.pdf',
    contentType: 'application/pdf',
    sizeBytes: 1024,
    pageCount: 10,
    processingStatus: 'Ready',
    uploadedAtUtc: '2026-07-15T00:00:00Z',
  },
]

const server = setupServer(
  http.get('*/api/v1/knowledge-bases/:id', () => HttpResponse.json(knowledgeBase)),
  http.get('*/api/v1/knowledge-bases/:id/folders', () => HttpResponse.json(folderTree)),
  http.get('*/api/v1/knowledge-bases/:id/documents', ({ request }) => {
    const folderId = new URL(request.url).searchParams.get('folderId')
    return HttpResponse.json<PagedResult<KnowledgeBaseDocument>>({
      items: folderId === ROOT_FOLDER_ID ? folderDocuments : [],
      nextCursor: null,
    })
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('KnowledgeBaseDetailPage (folder tree) accessibility (FR-039–FR-042, SC-010)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[`/knowledge-bases/${KNOWLEDGE_BASE_ID}`]}>
          <Routes>
            <Route path="/knowledge-bases/:id" element={<KnowledgeBaseDetailPage />} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('Drawings')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
