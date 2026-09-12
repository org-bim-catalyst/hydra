import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { ImageBlockRenderer } from './ImageBlock'

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderBlock(fileId = 'file-1', alt = 'Site aerial photograph') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ImageBlockRenderer block={{ kind: 'image', fileId, alt }} />
    </QueryClientProvider>,
  )
}

describe('ImageBlockRenderer (contracts/content-vocabulary.md "image" block, research D10)', () => {
  it('renders the image once the signed URL resolves, through the platform file-access mechanism', async () => {
    server.use(
      http.get('*/api/v1/documents/file-1/download', () => HttpResponse.json({ url: '/signed/file-1' })),
    )

    renderBlock()

    const img = await screen.findByAltText('Site aerial photograph')
    expect(img).toBeInTheDocument()
    expect(img.getAttribute('src')).toContain('/signed/file-1')
  })

  it('shows a visible unavailable state when the file cannot be reached, never a broken image', async () => {
    server.use(http.get('*/api/v1/documents/file-1/download', () => new HttpResponse(null, { status: 502 })))

    renderBlock()

    expect(await screen.findByText(/this image is unavailable/i)).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('shows a visible unavailable state when the user is not entitled to the file', async () => {
    server.use(http.get('*/api/v1/documents/file-1/download', () => new HttpResponse(null, { status: 403 })))

    renderBlock()

    expect(await screen.findByText(/this image is unavailable/i)).toBeInTheDocument()
  })
})
