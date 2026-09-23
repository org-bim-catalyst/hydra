import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { CustomModelDetail, OverwrittenFile } from '../../api/adminCustomModelsApi'
import { customModel } from './customModelFixtures'
import { OverwrittenFilesDialog } from './OverwrittenFilesDialog'

function detail(page: number, items: OverwrittenFile[], totalCount: number): CustomModelDetail {
  return { ...customModel({ overwrittenFileCount: totalCount }), overwrittenFiles: { items, page, pageSize: 2, totalCount } }
}

const firstPage = [
  { relativePath: 'config.json', previousSizeBytes: 812, overwrittenAtUtc: '2026-09-23T10:01:00Z' },
  { relativePath: 'onnx/text_encoder.onnx', previousSizeBytes: 3 * 2 ** 20, overwrittenAtUtc: '2026-09-23T10:02:00Z' },
]
const secondPage = [{ relativePath: 'voice_styles/F1.json', previousSizeBytes: 4096, overwrittenAtUtc: '2026-09-23T10:03:00Z' }]

const requestedPages: string[] = []
const server = setupServer(
  http.get('*/api/v1/admin/custom-models/:id', ({ request }) => {
    const page = new URL(request.url).searchParams.get('overwrittenPage') ?? '1'
    requestedPages.push(page)
    return HttpResponse.json(page === '2' ? detail(2, secondPage, 3) : detail(1, firstPage, 3))
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  requestedPages.length = 0
})
afterAll(() => server.close())

function renderDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <OverwrittenFilesDialog open modelId="model-1" modelName="supertonic-3" onClose={() => undefined} />
    </QueryClientProvider>,
  )
}

describe('OverwrittenFilesDialog', () => {
  it('lists each overwritten file with its previous size', async () => {
    renderDialog()

    expect(await screen.findByText('config.json')).toBeInTheDocument()
    expect(screen.getByText('812 bytes')).toBeInTheDocument()
    expect(screen.getByText('onnx/text_encoder.onnx')).toBeInTheDocument()
    expect(screen.getByText('3 MB')).toBeInTheDocument()
    expect(screen.getByText('Files overwritten by supertonic-3')).toBeInTheDocument()
  })

  it('pages through the list', async () => {
    renderDialog()
    await screen.findByText('config.json')

    fireEvent.click(screen.getByLabelText('Go to next page'))

    expect(await screen.findByText('voice_styles/F1.json')).toBeInTheDocument()
    expect(screen.getByText('4 KB')).toBeInTheDocument()
    expect(screen.queryByText('config.json')).not.toBeInTheDocument()
    expect(requestedPages).toEqual(['1', '2'])
  })

  it('shows an inline error with retry when the list cannot be loaded', async () => {
    let calls = 0
    server.use(
      http.get('*/api/v1/admin/custom-models/:id', () => {
        calls++
        return calls === 1
          ? HttpResponse.json({ title: 'Server error', status: 500, detail: 'Overwritten files are unavailable.' }, { status: 500 })
          : HttpResponse.json(detail(1, firstPage, 3))
      }),
    )
    renderDialog()

    expect(await screen.findByText('Overwritten files are unavailable.')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Retry'))

    expect(await screen.findByText('config.json')).toBeInTheDocument()
  })
})
