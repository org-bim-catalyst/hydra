import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { CustomModelSummary, DeploymentStatus, SourcePreview } from '../../api/adminCustomModelsApi'
import { AddCustomModelDialog } from './AddCustomModelDialog'

const SOURCE = 'https://huggingface.co/Supertone/supertonic-3'

const configuredStatus: DeploymentStatus = {
  isConfigured: true,
  transport: 'FTPS',
  maxDeploymentBytes: 20 * 2 ** 30,
  allowedDestinationPrefixes: ['Models'],
}

function previewFor(overrides: Partial<SourcePreview> = {}): SourcePreview {
  return {
    isValid: true,
    error: null,
    repositoryId: 'Supertone/supertonic-3',
    revision: 'main',
    ignoredFilePath: null,
    derivedName: 'supertonic-3',
    nameAvailable: true,
    ...overrides,
  }
}

const submitted = { id: 'model-1', name: 'supertonic-3', deploymentState: 'Queued' } as CustomModelSummary

const server = setupServer(
  http.get('*/api/v1/admin/custom-models/deployment-status', () => HttpResponse.json(configuredStatus)),
  http.post('*/api/v1/admin/custom-models/source-preview', () => HttpResponse.json(previewFor())),
  http.post('*/api/v1/admin/custom-models', () => HttpResponse.json(submitted, { status: 202 })),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderDialog(onSubmitted = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <AddCustomModelDialog open onClose={vi.fn()} onSubmitted={onSubmitted} />
    </QueryClientProvider>,
  )
  return { onSubmitted }
}

const deployButton = () => screen.getByText('Deploy').closest('button') as HTMLButtonElement

async function fillAndSubmit(source = SOURCE, destination = 'Models/supertonic-3') {
  fireEvent.change(screen.getByLabelText(/^Source/), { target: { value: source } })
  fireEvent.change(screen.getByLabelText(/^Destination/), { target: { value: destination } })
  await waitFor(() => expect(deployButton()).toBeEnabled())
  fireEvent.click(deployButton())
}

describe('AddCustomModelDialog', () => {
  it('shows only Source and Destination at first', async () => {
    renderDialog()

    expect(screen.getByLabelText(/^Source/)).toBeInTheDocument()
    expect(screen.getByLabelText(/^Destination/)).toBeInTheDocument()
    expect(screen.queryByLabelText(/^Name/)).not.toBeInTheDocument()
    expect(await screen.findByText(/Must start with Models\//)).toBeInTheDocument()
  })

  it('shows the parsed repository and keeps the Name field hidden when the derived name is free', async () => {
    renderDialog()

    fireEvent.change(screen.getByLabelText(/^Source/), { target: { value: SOURCE } })

    expect(await screen.findByText('Supertone/supertonic-3 @ main')).toBeInTheDocument()
    expect(screen.queryByLabelText(/^Name/)).not.toBeInTheDocument()
  })

  it('reveals the required Name field when the derived name is taken', async () => {
    server.use(
      http.post('*/api/v1/admin/custom-models/source-preview', () =>
        HttpResponse.json(previewFor({ nameAvailable: false })),
      ),
    )
    renderDialog()

    fireEvent.change(screen.getByLabelText(/^Source/), { target: { value: SOURCE } })

    expect(await screen.findByLabelText(/^Name/)).toBeRequired()
    expect(screen.getByText(/A model called supertonic-3 already exists/)).toBeInTheDocument()
  })

  it('reveals the required Name field when no name can be derived', async () => {
    server.use(
      http.post('*/api/v1/admin/custom-models/source-preview', () =>
        HttpResponse.json(previewFor({ derivedName: null, nameAvailable: false })),
      ),
    )
    renderDialog()

    fireEvent.change(screen.getByLabelText(/^Source/), { target: { value: SOURCE } })

    expect(await screen.findByLabelText(/^Name/)).toBeRequired()
  })

  it('shows the parse error from the preview under Source', async () => {
    server.use(
      http.post('*/api/v1/admin/custom-models/source-preview', () =>
        HttpResponse.json({
          ...previewFor(),
          isValid: false,
          error: 'Only huggingface.co repository URLs are supported.',
          repositoryId: null,
          derivedName: null,
          nameAvailable: false,
        }),
      ),
    )
    renderDialog()

    fireEvent.change(screen.getByLabelText(/^Source/), { target: { value: 'https://evil.example/model' } })

    expect(await screen.findByText('Only huggingface.co repository URLs are supported.')).toBeInTheDocument()
    expect(screen.queryByLabelText(/^Name/)).not.toBeInTheDocument()
  })

  it('tells the admin a single-file URL deploys the whole repository', async () => {
    server.use(
      http.post('*/api/v1/admin/custom-models/source-preview', () =>
        HttpResponse.json(previewFor({ ignoredFilePath: 'onnx/model.onnx' })),
      ),
    )
    renderDialog()

    fireEvent.change(screen.getByLabelText(/^Source/), { target: { value: `${SOURCE}/resolve/main/onnx/model.onnx` } })

    expect(
      await screen.findByText('Only whole repositories are deployed; onnx/model.onnx is ignored.'),
    ).toBeInTheDocument()
  })

  it('submits without a name when the derived one is used', async () => {
    let body: unknown
    server.use(
      http.post('*/api/v1/admin/custom-models', async ({ request }) => {
        body = await request.json()
        return HttpResponse.json(submitted, { status: 202 })
      }),
    )
    const { onSubmitted } = renderDialog()

    await fillAndSubmit()

    await waitFor(() => expect(onSubmitted).toHaveBeenCalledWith(submitted))
    expect(body).toEqual({ source: SOURCE, destination: 'Models/supertonic-3' })
  })

  it('shows server validation errors under the right field', async () => {
    server.use(
      http.post('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          {
            title: 'Validation failed',
            status: 400,
            detail: 'One or more validation errors occurred.',
            errors: { destination: ['The destination must be inside Models/.'] },
          },
          { status: 400 },
        ),
      ),
    )
    renderDialog()

    await fillAndSubmit(SOURCE, 'Other/supertonic-3')

    expect(await screen.findByText('The destination must be inside Models/.')).toBeInTheDocument()
    expect(screen.getByLabelText(/^Destination/)).toHaveAttribute('aria-invalid', 'true')
  })

  it('reveals the Name field when the server rejects the name', async () => {
    server.use(
      http.post('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          { title: 'Validation failed', status: 400, errors: { name: ['A model called supertonic-3 already exists.'] } },
          { status: 400 },
        ),
      ),
    )
    renderDialog()

    await fillAndSubmit()

    expect(await screen.findByLabelText(/^Name/)).toBeRequired()
    expect(screen.getByText('A model called supertonic-3 already exists.')).toBeInTheDocument()
  })

  it('shows a toast when submitting fails for any other reason', async () => {
    server.use(
      http.post('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          { title: 'Server error', status: 500, detail: 'The deployment could not be queued.' },
          { status: 500 },
        ),
      ),
    )
    renderDialog()

    await fillAndSubmit()

    expect(await screen.findByText('The deployment could not be queued.')).toBeInTheDocument()
  })

  it('says deployment is not configured and blocks submitting', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models/deployment-status', () =>
        HttpResponse.json({ ...configuredStatus, isConfigured: false, transport: null }),
      ),
    )
    renderDialog()

    expect(await screen.findByText(/Deployment not configured/)).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText(/^Source/), { target: { value: SOURCE } })
    fireEvent.change(screen.getByLabelText(/^Destination/), { target: { value: 'Models/x' } })
    expect(deployButton()).toBeDisabled()
  })

  it('warns when the server deploys over plain FTP', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models/deployment-status', () =>
        HttpResponse.json({ ...configuredStatus, transport: 'FTP' }),
      ),
    )
    renderDialog()

    expect(await screen.findByText(/plain FTP/)).toBeInTheDocument()
  })

  it('shows an error with retry when the deployment status cannot be loaded', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models/deployment-status', () =>
        HttpResponse.json({ title: 'Server error', status: 500, detail: 'Status unavailable.' }, { status: 500 }),
      ),
    )
    renderDialog()

    expect(await screen.findByText('Status unavailable.')).toBeInTheDocument()
    expect(screen.getByText('Retry')).toBeInTheDocument()
    expect(deployButton()).toBeDisabled()
  })
})
