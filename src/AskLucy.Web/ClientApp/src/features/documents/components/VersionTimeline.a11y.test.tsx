import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { VersionTimeline } from './VersionTimeline'

expect.extend(toHaveNoViolations)

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderTimeline() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <VersionTimeline documentId="doc-1" />
    </QueryClientProvider>,
  )
}

describe('VersionTimeline accessibility (FR-004, FR-008)', () => {
  it('has no automatically detectable a11y violations in the empty state', async () => {
    server.use(http.get('*/api/v1/documents/doc-1/versions', () => HttpResponse.json([])))
    const { container, findByText } = renderTimeline()

    await findByText('No version history yet')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in the error state', async () => {
    server.use(http.get('*/api/v1/documents/doc-1/versions', () => new HttpResponse(null, { status: 500 })))
    const { container, findByRole } = renderTimeline()

    await findByRole('alert')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
