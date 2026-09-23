import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { customModel } from './customModelFixtures'
import { OverwrittenFilesDialog } from './OverwrittenFilesDialog'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/admin/custom-models/:id', () =>
    HttpResponse.json({
      ...customModel({ overwrittenFileCount: 1 }),
      overwrittenFiles: {
        items: [{ relativePath: 'config.json', previousSizeBytes: 812, overwrittenAtUtc: '2026-09-23T10:01:00Z' }],
        page: 1,
        pageSize: 100,
        totalCount: 1,
      },
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('OverwrittenFilesDialog accessibility', () => {
  it('has no automatically detectable a11y violations with a file listed (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={queryClient}>
        <OverwrittenFilesDialog open modelId="model-1" modelName="supertonic-3" onClose={() => undefined} />
      </QueryClientProvider>,
    )

    await screen.findByText('config.json')

    // The dialog renders into a portal on document.body, outside the render container.
    expect(await axe(document.body)).toHaveNoViolations()
  })
})
