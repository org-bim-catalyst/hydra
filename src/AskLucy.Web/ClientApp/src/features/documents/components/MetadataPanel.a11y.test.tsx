import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { DocumentCategory, DocumentDetail } from '../api/documentsApi'
import { MetadataPanel } from './MetadataPanel'

expect.extend(toHaveNoViolations)

const categories: DocumentCategory[] = [
  { id: 'cat-1', name: 'Specifications', isSystemDefined: true },
  { id: 'cat-2', name: 'Drawings', isSystemDefined: true },
]

const detail: DocumentDetail = {
  summary: {
    id: 'doc-1',
    fileName: 'report.pdf',
    fileType: 'Pdf',
    sizeBytes: 204_800,
    processingStatus: 'Completed',
    folderId: null,
    categoryName: 'Specifications',
    languagePrimary: 'en',
    tags: ['final', 'reviewed'],
    isArchived: false,
    createdAtUtc: '2026-07-01T00:00:00Z',
    lastUpdatedAtUtc: '2026-07-02T00:00:00Z',
  },
  originalFileName: 'report.pdf',
  versionLabel: '1.0',
  rowVersion: 'AAAAAAAAB9E=',
  extractedText: 'Some extracted text.',
  extractedStructure: null,
  metadata: {
    title: 'Quarterly Report',
    author: 'Jane Doe',
    creationDate: '2026-07-01T00:00:00Z',
    modificationDate: '2026-07-01T00:00:00Z',
    keywords: 'quarterly, report',
    encoding: 'utf-8',
    isAutoExtracted: true,
    rowVersion: 'AAAAAAAAB9E=',
  },
  languages: [{ languageCode: 'en', role: 'Primary', confidenceScore: 0.98 }],
  classification: { categoryId: 'cat-1', categoryName: 'Specifications', source: 'Automatic', confidenceScore: 0.87 },
}

const server = setupServer(
  http.get('*/api/v1/documents/categories', () => HttpResponse.json(categories)),
  http.get('*/api/v1/documents/tags', () => HttpResponse.json(['final', 'reviewed', 'draft'])),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('MetadataPanel accessibility (FR-023, FR-026, FR-031, FR-031a, FR-032, FR-052)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByLabelText } = render(
      <QueryClientProvider client={queryClient}>
        <MetadataPanel documentId="doc-1" document={detail} />
      </QueryClientProvider>,
    )

    await findByLabelText('Title')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations when metadata has not been extracted yet', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MetadataPanel documentId="doc-1" document={{ ...detail, metadata: null }} />
      </QueryClientProvider>,
    )

    await findByText("Metadata isn't available yet — this document may still be processing.")

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
