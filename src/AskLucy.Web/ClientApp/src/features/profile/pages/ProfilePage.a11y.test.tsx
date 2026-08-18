import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { ProfilePage } from './ProfilePage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/profile', () =>
    HttpResponse.json({ email: 'lucy@example.com', firstName: 'Lucy', lastName: 'Ann' }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('ProfilePage accessibility (FR-004, SPEC-017 T043)', () => {
  it('has no automatically detectable a11y violations', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByLabelText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <ProfilePage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByLabelText('First name')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
