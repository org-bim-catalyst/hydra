import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { DocumentWorkspacePage } from './DocumentWorkspacePage'
import type { DocumentDetail, DocumentSummary } from '../api/documentsApi'

const DOCUMENT_ID = '11111111-1111-1111-1111-111111111111'

const documentSummary: DocumentSummary = {
  id: DOCUMENT_ID,
  fileName: 'report.pdf',
  fileType: 'Pdf',
  sizeBytes: 1000,
  processingStatus: 'Completed',
  folderId: null,
  categoryName: null,
  languagePrimary: null,
  tags: [],
  isArchived: false,
  createdAtUtc: '2026-09-01T00:00:00Z',
  lastUpdatedAtUtc: null,
}

const documentDetail: DocumentDetail = {
  summary: documentSummary,
  originalFileName: 'report.pdf',
  versionLabel: 'v1',
  rowVersion: '1',
  extractedText: null,
  extractedStructure: null,
  metadata: null,
  languages: [],
  classification: null,
}

const server = setupServer(
  http.get('*/api/v1/documents', () => HttpResponse.json({ items: [], nextCursor: null })),
  http.get('*/api/v1/documents/dashboard', () =>
    HttpResponse.json({
      pendingCount: 0,
      inProgressCount: 0,
      completedTodayCount: 0,
      failedCount: 0,
      retryQueue: [],
      statistics: {},
    }),
  ),
  http.get('*/api/v1/documents/folders/tree', () => HttpResponse.json([])),
  http.get(`*/api/v1/documents/${DOCUMENT_ID}`, () => HttpResponse.json(documentDetail)),
)

// Unmocked requests (processing status/history, versions, preview — DocumentDetailPanel's own
// sub-fetches) are bypassed rather than asserted on here: this suite only verifies the deep
// link opens the right panel, not every panel section's content.
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderAtPath(path: string) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <DocumentWorkspacePage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('DocumentWorkspacePage ?documentId= deep link (specs/067-notifications-communication-hub T092)', () => {
  it('opens the document detail panel for a valid ?documentId=', async () => {
    renderAtPath(`/documents?documentId=${DOCUMENT_ID}`)

    expect(await screen.findByRole('dialog', { name: 'report.pdf details' })).toBeInTheDocument()
  })

  it('shows an inline message for an unknown/inaccessible ?documentId=, without crashing', async () => {
    server.use(
      http.get('*/api/v1/documents/22222222-2222-2222-2222-222222222222', () =>
        HttpResponse.json({ title: 'Not found', status: 404 }, { status: 404 }),
      ),
    )

    renderAtPath('/documents?documentId=22222222-2222-2222-2222-222222222222')

    expect(await screen.findByText('This document is no longer available.')).toBeInTheDocument()
  })
})
