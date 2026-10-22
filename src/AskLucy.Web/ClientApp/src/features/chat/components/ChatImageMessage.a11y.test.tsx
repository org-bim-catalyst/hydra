import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { ChatImageMessage } from './ChatImageMessage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/documents/:id/download', () =>
    HttpResponse.json({
      url: '/api/v1/documents/versions/v1/download-content?exp=1&sig=2',
      fileName: 'map-snapshot-abc123.png',
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('ChatImageMessage accessibility', () => {
  it('has no automatically detectable a11y violations with the picture and its two controls', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container } = render(
      <QueryClientProvider client={queryClient}>
        <ChatImageMessage documentId="doc-1" alt="Al Safa Park 2" />
      </QueryClientProvider>,
    )

    await screen.findByRole('button', { name: 'Download Al Safa Park 2' })

    expect(await axe(container)).toHaveNoViolations()
  })
})
