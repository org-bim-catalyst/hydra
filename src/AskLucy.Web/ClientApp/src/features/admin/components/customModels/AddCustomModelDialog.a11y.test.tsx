import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { AddCustomModelDialog } from './AddCustomModelDialog'

expect.extend(toHaveNoViolations)

const SOURCE = 'https://huggingface.co/Supertone/supertonic-3'

function previewReturning(nameAvailable: boolean) {
  return http.post('*/api/v1/admin/custom-models/source-preview', () =>
    HttpResponse.json({
      isValid: true,
      error: null,
      repositoryId: 'Supertone/supertonic-3',
      revision: 'main',
      ignoredFilePath: null,
      derivedName: 'supertonic-3',
      nameAvailable,
    }),
  )
}

const server = setupServer(
  http.get('*/api/v1/admin/custom-models/deployment-status', () =>
    HttpResponse.json({
      isConfigured: true,
      transport: 'FTPS',
      maxDeploymentBytes: 2 ** 30,
      allowedDestinationPrefixes: ['Models'],
    }),
  ),
  previewReturning(true),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <AddCustomModelDialog open onClose={vi.fn()} />
    </QueryClientProvider>,
  )
}

// The dialog renders in a portal, so axe runs over the whole document rather than the container.
describe('AddCustomModelDialog accessibility', () => {
  it('has no automatically detectable a11y violations with only Source and Destination (constitution §10)', async () => {
    renderDialog()

    await screen.findByText(/Must start with Models\//)

    expect(await axe(document.body)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with the Name field showing (constitution §10)', async () => {
    server.use(previewReturning(false))
    renderDialog()

    fireEvent.change(screen.getByLabelText(/^Source/), { target: { value: SOURCE } })
    await screen.findByLabelText(/^Name/)

    expect(await axe(document.body)).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with inline errors showing (constitution §10)', async () => {
    server.use(
      http.post('*/api/v1/admin/custom-models', () =>
        HttpResponse.json(
          { title: 'Validation failed', status: 400, errors: { destination: ['The destination must be inside Models/.'] } },
          { status: 400 },
        ),
      ),
    )
    renderDialog()

    // Submitting empty shows the client-side errors; the server one follows once filled in.
    await waitFor(() => expect(screen.getByText('Deploy').closest('button')).toBeEnabled())
    fireEvent.click(screen.getByText('Deploy'))
    await screen.findByText('Enter a Hugging Face repository URL.')

    fireEvent.change(screen.getByLabelText(/^Source/), { target: { value: SOURCE } })
    fireEvent.change(screen.getByLabelText(/^Destination/), { target: { value: 'Other/x' } })
    fireEvent.click(screen.getByText('Deploy'))
    await screen.findByText('The destination must be inside Models/.')

    expect(await axe(document.body)).toHaveNoViolations()
  })
})
