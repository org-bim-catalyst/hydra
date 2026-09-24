import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import type { CustomModelSummary } from '../../api/adminCustomModelsApi'
import { customModel } from './customModelFixtures'
import { CustomModelsSection } from './CustomModelsSection'

const hub = vi.hoisted(() => ({
  state: { isLive: true, connectionLost: false, phaseById: {} as Record<string, string> },
}))
vi.mock('../../hooks/useCustomModelDeploymentsHub', () => ({
  useCustomModelDeploymentsHub: () => hub.state,
}))

const MANAGE = ['admin.custom-models.view', 'admin.custom-models.manage']

function page(items: CustomModelSummary[]) {
  return { items, page: 1, pageSize: 50, totalCount: items.length }
}

function sessionWith(permissions: string[]) {
  return http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions }),
  )
}

const server = setupServer(
  sessionWith(MANAGE),
  http.get('*/api/v1/admin/custom-models', () => HttpResponse.json(page([]))),
  deploymentStatus(),
)

function deploymentStatus(overrides: { isConfigured?: boolean; transport?: 'FTPS' | 'FTP' | null } = {}) {
  return http.get('*/api/v1/admin/custom-models/deployment-status', () =>
    HttpResponse.json({
      isConfigured: true,
      transport: 'FTPS',
      maxDeploymentBytes: 2 ** 30,
      allowedDestinationPrefixes: ['Models'],
      ...overrides,
    }),
  )
}

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())
beforeEach(() => {
  hub.state = { isLive: true, connectionLost: false, phaseById: {} }
})

let queryClient: QueryClient

function SectionUnderTest() {
  return (
    <QueryClientProvider client={queryClient}>
      <CustomModelsSection />
    </QueryClientProvider>
  )
}

function renderSection() {
  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<SectionUnderTest />)
}

describe('CustomModelsSection', () => {
  it('shows the empty state and the Add model button to a manager', async () => {
    renderSection()

    expect(await screen.findByText('No custom models yet.')).toBeInTheDocument()
    expect(await screen.findByText('Add model')).toBeInTheDocument()
  })

  it('opens the Add dialog from the Add model button', async () => {
    renderSection()

    fireEvent.click(await screen.findByText('Add model'))

    expect(await screen.findByText('Add custom model')).toBeInTheDocument()
  })

  it('hides the Add model button from a view-only admin', async () => {
    server.use(sessionWith(['admin.custom-models.view']))
    renderSection()

    expect(await screen.findByText('No custom models yet.')).toBeInTheDocument()
    expect(screen.queryByText('Add model')).not.toBeInTheDocument()
  })

  it('lists each model with its source, destination, size and state', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          page([
            customModel(),
            customModel({
              id: 'model-2',
              name: 'kokoro',
              repositoryId: 'hexgrad/Kokoro-82M',
              revision: 'v1.0',
              destination: 'Models/kokoro',
              deploymentState: 'Queued',
              totalBytes: null,
            }),
          ]),
        ),
      ),
    )
    renderSection()

    const first = (await screen.findByText('supertonic-3')).closest('tr') as HTMLElement
    expect(within(first).getByText('Supertone/supertonic-3@main')).toBeInTheDocument()
    expect(within(first).getByText('Models/supertonic-3')).toBeInTheDocument()
    expect(within(first).getByText('1.5 GB')).toBeInTheDocument()
    expect(within(first).getByText('Completed')).toBeInTheDocument()

    const second = screen.getByText('kokoro').closest('tr') as HTMLElement
    expect(within(second).getByText('hexgrad/Kokoro-82M@v1.0')).toBeInTheDocument()
    expect(within(second).getByText('—')).toBeInTheDocument()
    expect(within(second).getByText('Queued')).toBeInTheDocument()
  })

  it('lists a model that backs an on-server engine like any other, without a badge', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(page([customModel({ backsEngine: 'Supertonic' })])),
      ),
    )
    renderSection()

    const first = (await screen.findByText('supertonic-3')).closest('tr') as HTMLElement
    expect(within(first).queryByText(/^Backs /)).not.toBeInTheDocument()
  })

  it('shows an inline error with retry when the list cannot be loaded', async () => {
    let calls = 0
    server.use(
      http.get('*/api/v1/admin/custom-models', () => {
        calls++
        return calls === 1
          ? HttpResponse.json({ title: 'Server error', status: 500, detail: 'Custom models are unavailable.' }, { status: 500 })
          : HttpResponse.json(page([customModel()]))
      }),
    )
    renderSection()

    expect(await screen.findByText('Custom models are unavailable.')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Retry'))

    expect(await screen.findByText('supertonic-3')).toBeInTheDocument()
    expect(screen.queryByText('Custom models are unavailable.')).not.toBeInTheDocument()
  })

  it('shows the reconnecting banner only once the live connection is lost', async () => {
    const { rerender } = renderSection()
    await screen.findByText('No custom models yet.')
    expect(screen.queryByText('Live updates disconnected — reconnecting…')).not.toBeInTheDocument()

    hub.state = { isLive: false, connectionLost: true, phaseById: {} }
    rerender(<SectionUnderTest />)

    expect(screen.getByText('Live updates disconnected — reconnecting…')).toBeInTheDocument()
  })

  it('shows progress and Cancel only on in-progress rows', async () => {
    hub.state = { isLive: true, connectionLost: false, phaseById: { 'model-2': 'Downloading' } }
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          page([
            customModel(),
            customModel({
              id: 'model-2',
              name: 'kokoro',
              deploymentState: 'Transferring',
              canCancel: true,
              totalBytes: 4096,
              transferredBytes: 1024,
              totalFileCount: 2,
              completedFileCount: 0,
              currentFilePath: 'model.onnx',
              currentFileBytes: 10,
              currentFileTotalBytes: 100,
            }),
          ]),
        ),
      ),
    )
    renderSection()

    const running = (await screen.findByText('kokoro')).closest('tr') as HTMLElement
    expect(within(running).getByRole('progressbar', { name: 'Overall progress for kokoro' })).toBeInTheDocument()
    expect(within(running).getByText('Downloading model.onnx')).toBeInTheDocument()
    expect(within(running).getByRole('button', { name: 'Cancel deployment of kokoro' })).toBeInTheDocument()

    const done = screen.getByText('supertonic-3').closest('tr') as HTMLElement
    expect(within(done).queryByRole('progressbar')).not.toBeInTheDocument()
  })

  it('opens the overwritten files from a row that replaced some', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models', () => HttpResponse.json(page([customModel({ overwrittenFileCount: 2 })]))),
      http.get('*/api/v1/admin/custom-models/:id', () =>
        HttpResponse.json({
          ...customModel({ overwrittenFileCount: 2 }),
          overwrittenFiles: {
            items: [
              { relativePath: 'config.json', previousSizeBytes: 812, overwrittenAtUtc: '2026-09-23T10:01:00Z' },
              { relativePath: 'tts.json', previousSizeBytes: 90, overwrittenAtUtc: '2026-09-23T10:01:00Z' },
            ],
            page: 1,
            pageSize: 100,
            totalCount: 2,
          },
        }),
      ),
    )
    renderSection()

    fireEvent.click(await screen.findByText('2 files overwritten'))

    expect(await screen.findByText('config.json')).toBeInTheDocument()
    expect(screen.getByText('Files overwritten by supertonic-3')).toBeInTheDocument()
  })

  it('shows a failed row\'s reason and its kind', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          page([
            customModel({
              deploymentState: 'Failed',
              failureKind: 'TargetConnectionLost',
              failureReason: 'The connection to the deployment target was lost while uploading model.onnx.',
            }),
          ]),
        ),
      ),
    )
    renderSection()

    const row = (await screen.findByText('supertonic-3')).closest('tr') as HTMLElement
    expect(within(row).getByText('Failed')).toBeInTheDocument()
    expect(within(row).getByText('Connection lost')).toBeInTheDocument()
    expect(
      within(row).getByText('The connection to the deployment target was lost while uploading model.onnx.'),
    ).toBeInTheDocument()
  })

  it('shows an unknown failure kind as it is rather than hiding it', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          page([customModel({ deploymentState: 'Failed', failureKind: 'SomethingNew', failureReason: 'It broke.' })]),
        ),
      ),
    )
    renderSection()

    const row = (await screen.findByText('supertonic-3')).closest('tr') as HTMLElement
    expect(within(row).getByText('SomethingNew')).toBeInTheDocument()
    expect(within(row).getByText('It broke.')).toBeInTheDocument()
  })

  it('disables Add model and explains why when deployment is not configured', async () => {
    server.use(deploymentStatus({ isConfigured: false, transport: null }))
    renderSection()

    expect(
      await screen.findByText(
        'Deployment not configured. Ask whoever runs this server to set up the deployment target first.',
      ),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add model' })).toBeDisabled()
  })

  it('warns when the server deploys over plain FTP', async () => {
    server.use(deploymentStatus({ transport: 'FTP' }))
    renderSection()

    expect(await screen.findByText('Plain FTP')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add model' })).toBeEnabled()
  })

  it('shows no transport warning over FTPS', async () => {
    renderSection()

    expect(await screen.findByText('No custom models yet.')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Add model' })).toBeEnabled()
    expect(screen.queryByText('Plain FTP')).not.toBeInTheDocument()
  })

  it('makes a completed model available after confirmation', async () => {
    let sent: unknown = null
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(page([customModel({ availability: 'Unavailable' })])),
      ),
      http.put('*/api/v1/admin/custom-models/model-1/availability', async ({ request }) => {
        sent = await request.json()
        return HttpResponse.json(customModel({ availability: 'Available' }))
      }),
    )
    renderSection()

    fireEvent.click(await screen.findByRole('switch', { name: 'supertonic-3 available' }))
    fireEvent.click(await screen.findByText('Make available'))

    expect(await screen.findByText('supertonic-3 is now available.')).toBeInTheDocument()
    expect(sent).toEqual({ availability: 'Available' })
  })

  it('disables the availability switch with its reason when the model cannot be made available', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          page([
            customModel({
              deploymentState: 'Failed',
              availability: 'Unavailable',
              canMakeAvailable: false,
              availabilityBlockedReason: 'The deployment has not completed.',
            }),
          ]),
        ),
      ),
    )
    renderSection()

    const toggle = await screen.findByRole('switch', { name: 'supertonic-3 available' })
    expect(toggle).toBeDisabled()
    expect(screen.getByTitle('The deployment has not completed.')).toContainElement(toggle)
  })

  it('shows a toast naming the model already available for the repository', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(page([customModel({ availability: 'Unavailable' })])),
      ),
      http.put('*/api/v1/admin/custom-models/model-1/availability', () =>
        HttpResponse.json(
          {
            title: 'Conflict',
            status: 409,
            detail: '"supertonic-3-v1" is already available for Supertone/supertonic-3. Make it unavailable first.',
          },
          { status: 409 },
        ),
      ),
    )
    renderSection()

    fireEvent.click(await screen.findByRole('switch', { name: 'supertonic-3 available' }))
    fireEvent.click(await screen.findByText('Make available'))

    expect(
      await screen.findByText(
        '"supertonic-3-v1" is already available for Supertone/supertonic-3. Make it unavailable first.',
      ),
    ).toBeInTheDocument()
  })

  it('offers Remove only for a model that can be removed', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          page([
            customModel(),
            customModel({ id: 'model-2', name: 'kokoro', deploymentState: 'Failed', canRemove: true, canMakeAvailable: false }),
          ]),
        ),
      ),
    )
    renderSection()

    await screen.findByText('kokoro')
    expect(screen.queryByRole('button', { name: 'Remove supertonic-3' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Remove kokoro' })).toBeInTheDocument()
  })

  it('removes a model after confirmation', async () => {
    let removed = false
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          page(removed ? [] : [customModel({ deploymentState: 'Cancelled', canRemove: true, canMakeAvailable: false })]),
        ),
      ),
      http.delete('*/api/v1/admin/custom-models/model-1', () => {
        removed = true
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderSection()

    fireEvent.click(await screen.findByRole('button', { name: 'Remove supertonic-3' }))
    expect(await screen.findByText('Remove supertonic-3?')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Remove'))

    expect(await screen.findByText('No custom models yet.')).toBeInTheDocument()
    expect(removed).toBe(true)
  })

  it('keeps the row and shows the error when removal fails', async () => {
    server.use(
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(page([customModel({ deploymentState: 'Failed', canRemove: true, canMakeAvailable: false })])),
      ),
      http.delete('*/api/v1/admin/custom-models/model-1', () =>
        HttpResponse.json(
          { title: 'Bad request', status: 400, detail: 'Only a failed or cancelled model can be removed.' },
          { status: 400 },
        ),
      ),
    )
    renderSection()

    fireEvent.click(await screen.findByRole('button', { name: 'Remove supertonic-3' }))
    fireEvent.click(await screen.findByText('Remove'))

    expect(await screen.findByText('Only a failed or cancelled model can be removed.')).toBeInTheDocument()
    expect(screen.getByText('supertonic-3')).toBeInTheDocument()
  })

  it('shows availability as text, with no manage controls, to a view-only admin', async () => {
    server.use(
      sessionWith(['admin.custom-models.view']),
      http.get('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          page([
            customModel(),
            customModel({ id: 'model-2', name: 'kokoro', deploymentState: 'Failed', canRemove: true, canMakeAvailable: false }),
          ]),
        ),
      ),
    )
    renderSection()

    const row = (await screen.findByText('supertonic-3')).closest('tr') as HTMLElement
    expect(within(row).getByText('Available')).toBeInTheDocument()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Remove/ })).not.toBeInTheDocument()
  })
})
