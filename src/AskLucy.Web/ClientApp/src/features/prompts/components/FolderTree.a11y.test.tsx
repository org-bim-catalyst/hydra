import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { PromptFolder } from '../api/promptFoldersApi'
import { FolderTree } from './FolderTree'

expect.extend(toHaveNoViolations)

const folders: PromptFolder[] = [
  { id: '11111111-1111-1111-1111-111111111111', parentFolderId: null, name: 'Marketing', depth: 0 },
  { id: '22222222-2222-2222-2222-222222222222', parentFolderId: '11111111-1111-1111-1111-111111111111', name: 'Campaigns', depth: 1 },
]

const server = setupServer(http.get('*/api/v1/prompt-folders', () => HttpResponse.json(folders)))

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('FolderTree accessibility (spec.md User Story 4)', () => {
  it('has no automatically detectable a11y violations (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <FolderTree selectedFolderId={null} onSelectFolder={() => {}} />
      </QueryClientProvider>,
    )

    await findByText('Campaigns')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
