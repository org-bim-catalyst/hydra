import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { MemoryDetail } from '../api/memoryApi'
import { MemoryConflictDialog } from './MemoryConflictDialog'

expect.extend(toHaveNoViolations)

const memory: MemoryDetail = {
  id: 'memory-1',
  category: 'PersonalFact',
  content: 'Prefers React over Angular for new frontend work',
  state: 'Active',
  isSensitive: false,
  projectId: null,
  importance: 0.7,
  confidence: 0.85,
  history: [],
  openConflict: {
    id: 'conflict-1',
    conflictType: 'AmbiguousSupersedeOrSupplement',
    existingMemoryId: 'memory-0',
    newMemoryId: 'memory-1',
    detectedAtUtc: '2026-08-08T14:00:00Z',
  },
}

const server = setupServer(http.get('*/api/v1/memories/memory-1', () => HttpResponse.json(memory)))

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('MemoryConflictDialog accessibility (tasks.md T099, spec.md FR-016, User Story 6 AC2/AC3)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { baseElement, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryConflictDialog open memoryId="memory-1" onClose={vi.fn()} />
      </QueryClientProvider>,
    )

    await findByText(memory.content)

    const results = await axe(baseElement)
    expect(results).toHaveNoViolations()
  })
})
