import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { MemoryPreferences } from '../api/memoryApi'
import { MemoryPreferencesPanel } from './MemoryPreferencesPanel'

expect.extend(toHaveNoViolations)

const preferences: MemoryPreferences = {
  memoryEnabled: true,
  categories: [
    { category: 'UserPreference', approvalMode: 'Automatic', isEnabled: true },
    { category: 'PersonalFact', approvalMode: 'Manual', isEnabled: true },
    { category: 'ProjectContext', approvalMode: 'Automatic', isEnabled: false },
    { category: 'ConversationDerived', approvalMode: 'Disabled', isEnabled: true },
  ],
}

const server = setupServer(http.get('*/api/v1/memories/preferences', () => HttpResponse.json(preferences)))

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('MemoryPreferencesPanel accessibility (tasks.md T099, spec.md FR-007, FR-022-FR-025, User Story 3/4)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByLabelText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryPreferencesPanel />
      </QueryClientProvider>,
    )

    await findByLabelText('Let Lucy remember things about me')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
