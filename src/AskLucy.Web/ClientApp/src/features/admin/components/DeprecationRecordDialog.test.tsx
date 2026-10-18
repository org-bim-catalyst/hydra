import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ModelDeprecationDetail } from '../api/modelDeprecationsApi'
import * as deprecationsApi from '../api/modelDeprecationsApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { DeprecationRecordDialog } from './DeprecationRecordDialog'

// Text queries inside the dialog, not getByRole: jsdom's getComputedStyle crashes on MUI
// portal content under role queries (jsdom_getcomputedstyle_crash_mui_dialog precedent).
vi.mock('../api/modelDeprecationsApi', async () => {
  const actual = await vi.importActual<typeof deprecationsApi>('../api/modelDeprecationsApi')
  return {
    ...actual,
    getModelDeprecation: vi.fn(),
    acceptModelReplacement: vi.fn().mockResolvedValue(undefined),
    changeModelReplacement: vi.fn().mockResolvedValue({ batchId: 'batch-2' }),
  }
})

vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return {
    ...actual,
    getModels: vi.fn().mockResolvedValue([
      {
        id: 'model-new', modelKey: 'gpt-5', displayName: 'GPT-5', contextWindowTokens: null,
        maxOutputTokens: null, capabilities: {
          streaming: true, vision: false, functionCalling: false, jsonMode: false, reasoning: false,
          embeddings: false, imageInput: false, imageOutput: false, audio: false,
        }, pricing: null, releaseDate: null, status: 'Available',
      },
      {
        id: 'model-gone', modelKey: 'gpt-4', displayName: 'GPT-4', contextWindowTokens: null,
        maxOutputTokens: null, capabilities: {
          streaming: true, vision: false, functionCalling: false, jsonMode: false, reasoning: false,
          embeddings: false, imageInput: false, imageOutput: false, audio: false,
        }, pricing: null, releaseDate: null, status: 'Unavailable',
      },
    ]),
  }
})

const detail: ModelDeprecationDetail = {
  id: 'dep-1',
  batchId: 'batch-1',
  modelId: 'model-old',
  modelKey: 'gpt-4-turbo',
  displayName: 'GPT-4 Turbo',
  providerId: 'provider-1',
  providerName: 'OpenAI',
  reason: 'RemovedFromVendor',
  replacementModelId: 'model-new',
  replacementDisplayName: 'GPT-5',
  replacementSource: 'Proposed',
  validationStatus: 'AwaitingValidation',
  validatedByUserId: null,
  validatedAtUtc: null,
  reassignedCount: 1,
  openFlagCount: 0,
  userImpactStatus: 'Completed',
  notifiedUserCount: 2,
  confirmedAtUtc: '2026-10-18T09:00:00Z',
  confirmedByUserId: 'admin-1',
  platformDefaults: [
    {
      targetKind: 'ProviderDefault', capability: null, outcome: 'Reassigned',
      newModelId: 'model-new', newModelDisplayName: 'GPT-5', flagReason: null,
      isOpenFlag: false, resolvedAtUtc: null,
    },
  ],
}

function renderDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <DeprecationRecordDialog deprecationId="dep-1" providerId="provider-1" open onClose={() => {}} />
    </QueryClientProvider>,
  )
}

describe('DeprecationRecordDialog', () => {
  beforeEach(() => {
    vi.mocked(deprecationsApi.getModelDeprecation).mockResolvedValue(detail)
    vi.mocked(deprecationsApi.acceptModelReplacement).mockClear()
    vi.mocked(deprecationsApi.changeModelReplacement).mockClear()
  })

  it('shows the record and lets the administrator accept the replacement', async () => {
    renderDialog()

    expect(await screen.findByText(/GPT-4 Turbo · OpenAI/)).toBeTruthy()
    expect(screen.getByText(/Replacement: GPT-5/)).toBeTruthy()

    fireEvent.click(screen.getByText('Accept replacement'))

    await waitFor(() => expect(deprecationsApi.acceptModelReplacement).toHaveBeenCalledWith('dep-1'))
    expect(await screen.findByText('Replacement accepted.')).toBeTruthy()
  })

  it('offers only Available models to change the replacement to', async () => {
    renderDialog()

    await screen.findByText(/Replacement: GPT-5/)

    fireEvent.mouseDown(screen.getByLabelText('Change to'))

    expect(await screen.findByText('GPT-5')).toBeTruthy()
    expect(screen.queryByText('GPT-4')).toBeNull()
  })

  it('closes the validation actions once the replacement has been accepted', async () => {
    vi.mocked(deprecationsApi.getModelDeprecation).mockResolvedValue({
      ...detail,
      validationStatus: 'Accepted',
      validatedAtUtc: '2026-10-18T10:00:00Z',
      validatedByUserId: 'admin-1',
    })

    renderDialog()

    expect(await screen.findByText(/Validation: Accepted/)).toBeTruthy()
    expect(screen.queryByText('Accept replacement')).toBeNull()
  })

  it('surfaces a failed validation instead of failing silently', async () => {
    vi.mocked(deprecationsApi.acceptModelReplacement).mockRejectedValueOnce(new Error('Replacement changed'))
    renderDialog()

    fireEvent.click(await screen.findByText('Accept replacement'))

    expect(await screen.findByText('Replacement changed')).toBeTruthy()
  })
})
