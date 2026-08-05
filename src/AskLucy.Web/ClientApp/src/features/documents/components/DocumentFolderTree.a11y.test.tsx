import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { DocumentFolder } from '../api/documentsApi'
import { DocumentFolderTree } from './DocumentFolderTree'

expect.extend(toHaveNoViolations)

const folders: DocumentFolder[] = [
  { id: 'folder-1', name: 'Drawings', parentFolderId: null, depth: 0, documentCount: 3 },
  { id: 'folder-2', name: 'Specifications', parentFolderId: 'folder-1', depth: 1, documentCount: 0 },
]

const server = setupServer(http.get('*/api/v1/documents/folders/tree', () => HttpResponse.json(folders)))

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('DocumentFolderTree accessibility (FR-033, FR-052)', () => {
  it('has no automatically detectable a11y violations with folders loaded', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <DocumentFolderTree selectedFolderId={null} onSelectFolder={vi.fn()} />
      </QueryClientProvider>,
    )

    await findByText('Drawings (3)')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
