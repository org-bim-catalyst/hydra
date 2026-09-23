import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { CustomModelSummary, TransferPhase } from '../../api/adminCustomModelsApi'
import { customModel } from './customModelFixtures'
import { CustomModelProgress } from './CustomModelProgress'

const transferring = customModel({
  deploymentState: 'Transferring',
  canCancel: true,
  totalBytes: 2048,
  transferredBytes: 512,
  totalFileCount: 4,
  completedFileCount: 1,
  currentFilePath: 'onnx/text_encoder.onnx',
  currentFileBytes: 100,
  currentFileTotalBytes: 400,
  finishedAtUtc: null,
})

let cancelCalls = 0
const server = setupServer(
  http.post('*/api/v1/admin/custom-models/:id/actions/cancel', () => {
    cancelCalls++
    return HttpResponse.json({ ...transferring }, { status: 202 })
  }),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  cancelCalls = 0
})
afterAll(() => server.close())

function renderProgress(model: CustomModelSummary, { canManage = true, phase }: { canManage?: boolean; phase?: TransferPhase } = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <CustomModelProgress model={model} canManage={canManage} phase={phase} />
    </QueryClientProvider>,
  )
}

describe('CustomModelProgress', () => {
  it('shows overall and per-file progress with the current phase', () => {
    renderProgress(transferring, { phase: 'Uploading' })

    const overall = screen.getByRole('progressbar', { name: 'Overall progress for supertonic-3' })
    expect(overall).toHaveAttribute('aria-valuenow', '25')
    expect(screen.getByText('512 bytes of 2 KB · 1 of 4 files')).toBeInTheDocument()

    const file = screen.getByRole('progressbar', { name: 'Progress for onnx/text_encoder.onnx' })
    expect(file).toHaveAttribute('aria-valuenow', '25')
    expect(screen.getByText('Uploading onnx/text_encoder.onnx')).toBeInTheDocument()
  })

  it('falls back to a transfer label before the first progress event names the phase', () => {
    renderProgress(transferring)

    expect(screen.getByText('Transferring onnx/text_encoder.onnx')).toBeInTheDocument()
  })

  it('shows an indeterminate bar while the repository is being listed', () => {
    renderProgress(customModel({ deploymentState: 'Listing', canCancel: true, totalBytes: null, totalFileCount: null }))

    const bar = screen.getByRole('progressbar', { name: 'Overall progress for supertonic-3' })
    expect(bar).not.toHaveAttribute('aria-valuenow')
    expect(screen.getByText('Listing files…')).toBeInTheDocument()
  })

  it('shows a queued deployment as waiting to start', () => {
    renderProgress(customModel({ deploymentState: 'Queued', canCancel: true, totalBytes: null, totalFileCount: null }))

    expect(screen.getByText('Waiting to start…')).toBeInTheDocument()
  })

  it('offers Cancel to a manager and cancels only after confirmation', async () => {
    renderProgress(transferring)

    fireEvent.click(screen.getByRole('button', { name: 'Cancel deployment of supertonic-3' }))
    expect(await screen.findByText('Cancel this deployment?')).toBeInTheDocument()
    expect(cancelCalls).toBe(0)

    fireEvent.click(screen.getByText('Cancel deployment'))

    await waitFor(() => expect(cancelCalls).toBe(1))
  })

  it('does nothing when the confirmation is dismissed', async () => {
    renderProgress(transferring)

    fireEvent.click(screen.getByRole('button', { name: 'Cancel deployment of supertonic-3' }))
    fireEvent.click(await screen.findByText('Keep running'))

    await waitFor(() => expect(screen.queryByText('Cancel this deployment?')).not.toBeInTheDocument())
    expect(cancelCalls).toBe(0)
  })

  it('hides Cancel from a view-only admin', () => {
    renderProgress(transferring, { canManage: false })

    expect(screen.queryByRole('button', { name: /Cancel deployment/ })).not.toBeInTheDocument()
  })

  it('hides Cancel once the deployment can no longer be cancelled', () => {
    renderProgress({ ...transferring, canCancel: false })

    expect(screen.queryByRole('button', { name: /Cancel deployment/ })).not.toBeInTheDocument()
  })

  it('shows a toast when the cancel request fails', async () => {
    server.use(
      http.post('*/api/v1/admin/custom-models/:id/actions/cancel', () =>
        HttpResponse.json(
          { title: 'Deployment not in progress', status: 409, detail: "This deployment is already Completed and can't be cancelled." },
          { status: 409 },
        ),
      ),
    )
    renderProgress(transferring)

    fireEvent.click(screen.getByRole('button', { name: 'Cancel deployment of supertonic-3' }))
    fireEvent.click(await screen.findByText('Cancel deployment'))

    expect(await screen.findByText("This deployment is already Completed and can't be cancelled.")).toBeInTheDocument()
  })
})
