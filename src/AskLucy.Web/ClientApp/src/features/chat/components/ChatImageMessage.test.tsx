import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { useFloatingPanelStore } from '../../../viewer/panels/store/floatingPanelStore'
import { ChatImageMessage } from './ChatImageMessage'

const documentId = 'doc-1'

const server = setupServer(
  http.get('*/api/v1/documents/:id/download', () =>
    HttpResponse.json({
      url: '/api/v1/documents/versions/v1/download-content?exp=1&sig=2',
      fileName: 'map-snapshot-abc123.png',
    }),
  ),
  http.get('*/api/v1/documents/versions/:id/download-content', () =>
    new HttpResponse(new Blob([new Uint8Array([1, 2, 3])], { type: 'image/png' }), { status: 200 }),
  ),
)

function renderImage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ChatImageMessage documentId={documentId} alt="Al Safa Park 2" />
    </QueryClientProvider>,
  )
}

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  useFloatingPanelStore.setState({ panels: [], closedPanels: [], cascadeIndex: 0 })
})
afterAll(() => server.close())

beforeEach(() => {
  // jsdom has no object URLs; the download path needs one to hand to the anchor.
  URL.createObjectURL = vi.fn(() => 'blob:mock')
  URL.revokeObjectURL = vi.fn()
})

describe('ChatImageMessage', () => {
  it('shows the picture with a control that opens it and a control that downloads it', async () => {
    renderImage()

    // The picture itself is the button that opens the panel; the two overlaid controls are the
    // visible indications of what can be done with it.
    expect(await screen.findByRole('button', { name: 'Al Safa Park 2' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open Al Safa Park 2' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Download Al Safa Park 2' })).toBeInTheDocument()
  })

  it('opens the picture in a floating content panel when the picture is clicked', async () => {
    renderImage()

    fireEvent.click(await screen.findByRole('button', { name: 'Al Safa Park 2' }))

    const panels = useFloatingPanelStore.getState().panels
    expect(panels).toHaveLength(1)
    expect(panels[0].title).toBe('Al Safa Park 2')
    expect(panels[0].kind).toBe('content')
    // The image block names the document, so the panel resolves it through the same
    // entitlement-checked download endpoint the bubble itself uses.
    expect(panels[0].content?.blocks).toEqual([
      { kind: 'image', fileId: documentId, alt: 'Al Safa Park 2' },
    ])
  })

  it('opens the same panel from the overlay control', async () => {
    renderImage()

    fireEvent.click(await screen.findByRole('button', { name: 'Open Al Safa Park 2' }))

    expect(useFloatingPanelStore.getState().panels).toHaveLength(1)
  })

  it('reuses the same panel when the same picture is opened again', async () => {
    renderImage()

    fireEvent.click(await screen.findByRole('button', { name: 'Al Safa Park 2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Al Safa Park 2' }))

    expect(useFloatingPanelStore.getState().panels).toHaveLength(1)
  })

  it('saves the image to disk under the place name, not the server document name', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    renderImage()

    fireEvent.click(await screen.findByRole('button', { name: 'Download Al Safa Park 2' }))

    await waitFor(() => expect(click).toHaveBeenCalled())
    const link = click.mock.instances[0] as HTMLAnchorElement
    expect(link.download).toBe('Al Safa Park 2.png')
    expect(link.href).toContain('blob:mock')
  })

  it('replaces the characters a filename cannot carry', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(
      <QueryClientProvider client={queryClient}>
        <ChatImageMessage documentId={documentId} alt='Al Safa/Park: 2' />
      </QueryClientProvider>,
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Download Al Safa/Park: 2' }))

    await waitFor(() => expect(click).toHaveBeenCalled())
    const link = click.mock.instances[0] as HTMLAnchorElement
    expect(link.download).toBe('Al Safa-Park- 2.png')
  })

  it('says so when the download fails rather than failing silently', async () => {
    server.use(
      http.get('*/api/v1/documents/:id/download', () => HttpResponse.error()),
    )
    renderImage()

    fireEvent.click(await screen.findByRole('button', { name: 'Download Al Safa Park 2' }))

    expect(await screen.findByText('The image could not be downloaded.')).toBeInTheDocument()
  })
})
