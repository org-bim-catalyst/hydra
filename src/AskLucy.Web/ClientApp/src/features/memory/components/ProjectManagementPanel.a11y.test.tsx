import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { ProjectsResult } from '../api/projectsApi'
import { ProjectManagementPanel } from './ProjectManagementPanel'

expect.extend(toHaveNoViolations)

const projects: ProjectsResult = {
  items: [
    { id: 'project-1', name: 'Riverside Tower', createdAtUtc: '2026-07-01T09:00:00Z' },
    { id: 'project-2', name: 'Harbor Bridge Retrofit', createdAtUtc: '2026-07-15T09:00:00Z' },
  ],
  nextCursor: null,
}

const server = setupServer(http.get('*/api/v1/projects', () => HttpResponse.json(projects)))

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('ProjectManagementPanel accessibility (tasks.md T099, spec.md FR-002a, User Story 5)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <ProjectManagementPanel />
      </QueryClientProvider>,
    )

    await findByText('Riverside Tower')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
