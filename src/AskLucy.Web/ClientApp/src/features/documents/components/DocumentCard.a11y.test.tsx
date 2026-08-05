import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { DocumentSummary } from '../api/documentsApi'
import { DocumentCard } from './DocumentCard'

expect.extend(toHaveNoViolations)

const document: DocumentSummary = {
  id: 'doc-1',
  fileName: 'report.pdf',
  fileType: 'Pdf',
  sizeBytes: 204_800,
  processingStatus: 'Completed',
  folderId: null,
  categoryName: 'Specifications',
  languagePrimary: 'en',
  tags: ['final'],
  isArchived: false,
  createdAtUtc: '2026-07-01T00:00:00Z',
  lastUpdatedAtUtc: '2026-07-02T00:00:00Z',
}

const server = setupServer(http.get('*/api/v1/documents/folders/tree', () => HttpResponse.json([])))

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('DocumentCard accessibility (User Story 1, FR-052)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <DocumentCard document={document} view="Active" onOpenDetail={vi.fn()} />
      </QueryClientProvider>,
    )

    await findByText('report.pdf')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('opens the detail panel from the keyboard alone, not just a mouse click', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const onOpenDetail = vi.fn()
    const { getByRole } = render(
      <QueryClientProvider client={queryClient}>
        <DocumentCard document={document} view="Active" onOpenDetail={onOpenDetail} />
      </QueryClientProvider>,
    )

    const title = getByRole('button', { name: 'Open details for report.pdf' })
    title.focus()
    fireEvent.keyDown(title, { key: 'Enter' })

    expect(onOpenDetail).toHaveBeenCalledWith(document)
  })
})
