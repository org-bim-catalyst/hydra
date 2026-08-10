import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { PromptVersionSummary } from '../api/promptVersionsApi'
import { VersionHistory } from './VersionHistory'

expect.extend(toHaveNoViolations)

const PROMPT_ID = '11111111-1111-1111-1111-111111111111'

const versions: PromptVersionSummary[] = [
  { id: '22222222-2222-2222-2222-222222222222', versionNumber: 2, changeDescription: 'Added language variable', createdBy: 'user-1', createdAtUtc: '2026-08-02T00:00:00Z' },
  { id: '33333333-3333-3333-3333-333333333333', versionNumber: 1, changeDescription: null, createdBy: 'user-1', createdAtUtc: '2026-08-01T00:00:00Z' },
]

const server = setupServer(http.get(`*/api/v1/prompts/${PROMPT_ID}/versions`, () => HttpResponse.json(versions)))

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('VersionHistory/VersionComparison accessibility (spec.md FR-032, User Story 3)', () => {
  it('has no automatically detectable a11y violations (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByTestId } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <VersionHistory promptId={PROMPT_ID} />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByTestId('version-history-list')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
