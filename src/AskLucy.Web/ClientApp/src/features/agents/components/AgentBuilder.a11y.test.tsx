import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { AgentBuilder } from './AgentBuilder'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/ai/providers', () =>
    HttpResponse.json([{ id: 'provider-1', providerKey: 'openai', displayName: 'OpenAI' }]),
  ),
  http.get('*/api/v1/ai/models', () =>
    HttpResponse.json([{ id: 'model-1', providerId: 'provider-1', modelKey: 'gpt-5', displayName: 'GPT-5' }]),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AgentBuilder accessibility (spec.md FR-001-FR-006, User Story 1)', () => {
  it('has no automatically detectable a11y violations in create mode', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByRole } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AgentBuilder />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByRole('heading', { name: 'New Agent' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
