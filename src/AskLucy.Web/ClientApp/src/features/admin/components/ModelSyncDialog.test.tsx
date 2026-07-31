import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ApplyProviderModelSyncResult, ProviderModelSyncDiff } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { ModelSyncDialog } from './ModelSyncDialog'

// Same reasoning as AiProviderActionsMenu.test.tsx: MUI's Paper-based Dialog surfaces
// render an inline `--Paper-shadow` custom property jsdom's CSS length parser cannot
// resolve — use fireEvent + text/DOM queries instead of getByRole for anything inside it.
vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return {
    ...actual,
    syncModels: vi.fn(),
    applyModelSync: vi.fn(),
  }
})

const capabilities = {
  streaming: true,
  vision: false,
  functionCalling: true,
  jsonMode: true,
  reasoning: false,
  embeddings: false,
  imageInput: false,
  imageOutput: false,
  audio: false,
}

const diffWithChanges: ProviderModelSyncDiff = {
  added: [
    { modelKey: 'gpt-5', displayName: 'GPT-5', contextWindowTokens: 200000, maxOutputTokens: 32000, capabilities },
    { modelKey: 'gpt-5-mini', displayName: 'GPT-5 Mini', contextWindowTokens: 200000, maxOutputTokens: 16000, capabilities },
  ],
  removedFromVendor: [
    { id: 'model-1', modelKey: 'gpt-3.5', displayName: 'GPT-3.5' },
    { id: 'model-2', modelKey: 'gpt-4-old', displayName: 'GPT-4 Old' },
  ],
}

const emptyDiff: ProviderModelSyncDiff = { added: [], removedFromVendor: [] }

const emptyResult: ApplyProviderModelSyncResult = { appliedModelKeys: [], failed: [] }

function renderDialog(onClose = vi.fn()) {
  const queryClient = new QueryClient()
  return {
    onClose,
    ...render(
      <QueryClientProvider client={queryClient}>
        <ModelSyncDialog providerId="provider-1" providerDisplayName="OpenAI" open onClose={onClose} />
      </QueryClientProvider>,
    ),
  }
}

async function openDiff() {
  vi.mocked(adminAiProvidersApi.syncModels).mockResolvedValue(diffWithChanges)
  renderDialog()
  fireEvent.click(screen.getByText('Check for updates'))
  await screen.findByText('GPT-5')
}

describe('ModelSyncDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(adminAiProvidersApi.applyModelSync).mockResolvedValue(emptyResult)
  })

  it('shows the diff after checking for updates', async () => {
    await openDiff()

    expect(screen.getByText('GPT-3.5')).toBeInTheDocument()
    expect(adminAiProvidersApi.applyModelSync).not.toHaveBeenCalled()
  })

  it('shows a clear "nothing to review" state when the catalog already matches the provider', async () => {
    vi.mocked(adminAiProvidersApi.syncModels).mockResolvedValue(emptyDiff)
    renderDialog()

    fireEvent.click(screen.getByText('Check for updates'))

    expect(await screen.findByText(/nothing to review/i)).toBeInTheDocument()
    expect(screen.queryByText('Confirm')).not.toBeInTheDocument()
  })

  it('dismissing the reviewed diff calls no apply', async () => {
    const onClose = vi.fn()
    vi.mocked(adminAiProvidersApi.syncModels).mockResolvedValue(diffWithChanges)
    renderDialog(onClose)

    fireEvent.click(screen.getByText('Check for updates'))
    await screen.findByText('GPT-5')
    fireEvent.click(screen.getByText('Dismiss'))

    expect(adminAiProvidersApi.applyModelSync).not.toHaveBeenCalled()
    expect(onClose).toHaveBeenCalled()
  })

  it('filters both diff sides by name/key, live as the administrator types', async () => {
    await openDiff()

    fireEvent.change(screen.getByLabelText('Filter by name or key'), { target: { value: 'mini' } })

    expect(screen.queryByText('GPT-5')).not.toBeInTheDocument()
    expect(screen.getByText('GPT-5 Mini')).toBeInTheDocument()
    expect(screen.queryByText('GPT-3.5')).not.toBeInTheDocument()
    expect(screen.queryByText('GPT-4 Old')).not.toBeInTheDocument()
  })

  it('clearing the filter restores the full list', async () => {
    await openDiff()

    fireEvent.change(screen.getByLabelText('Filter by name or key'), { target: { value: 'mini' } })
    fireEvent.change(screen.getByLabelText('Filter by name or key'), { target: { value: '' } })

    expect(screen.getByText('GPT-5')).toBeInTheDocument()
    expect(screen.getByText('GPT-3.5')).toBeInTheDocument()
  })

  it('shows a "no rows match" message per side when the filter matches nothing, distinct from the empty-diff state', async () => {
    await openDiff()

    fireEvent.change(screen.getByLabelText('Filter by name or key'), { target: { value: 'zzz-no-match' } })

    expect(screen.getAllByText(/no rows match your search/i)).toHaveLength(2)
    expect(screen.queryByText(/nothing to review/i)).not.toBeInTheDocument()
  })

  it('checking a subset and confirming calls apply with exactly that subset, not the full diff', async () => {
    await openDiff()

    fireEvent.click(screen.getByLabelText('Select GPT-5'))
    fireEvent.click(screen.getByLabelText('Select GPT-3.5'))
    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() =>
      expect(adminAiProvidersApi.applyModelSync).toHaveBeenCalledWith('provider-1', {
        added: [diffWithChanges.added[0]],
        removedFromVendor: [diffWithChanges.removedFromVendor[0]],
      }),
    )
  })

  it('"select all" on a side selects only that side\'s currently-visible (filtered) rows', async () => {
    await openDiff()

    fireEvent.change(screen.getByLabelText('Filter by name or key'), { target: { value: 'mini' } })
    fireEvent.click(screen.getAllByText('Select all')[0])
    fireEvent.change(screen.getByLabelText('Filter by name or key'), { target: { value: '' } })
    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() =>
      expect(adminAiProvidersApi.applyModelSync).toHaveBeenCalledWith('provider-1', {
        added: [diffWithChanges.added[1]],
        removedFromVendor: [],
      }),
    )
  })

  it('deselecting one row after "select all" leaves the rest selected', async () => {
    await openDiff()

    fireEvent.click(screen.getAllByText('Select all')[0])
    fireEvent.click(screen.getByLabelText('Select GPT-5'))
    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() =>
      expect(adminAiProvidersApi.applyModelSync).toHaveBeenCalledWith('provider-1', {
        added: [diffWithChanges.added[1]],
        removedFromVendor: [],
      }),
    )
  })

  it('renders a live selected count across both sides', async () => {
    await openDiff()

    expect(screen.getByText('0 selected')).toBeInTheDocument()

    fireEvent.click(screen.getByLabelText('Select GPT-5'))
    fireEvent.click(screen.getByLabelText('Select GPT-3.5'))

    expect(screen.getByText('2 selected')).toBeInTheDocument()
  })

  it('preserves a selection hidden by a later filter change, and still applies it on confirm (FR-005)', async () => {
    await openDiff()

    fireEvent.click(screen.getByLabelText('Select GPT-5'))
    fireEvent.change(screen.getByLabelText('Filter by name or key'), { target: { value: 'mini' } })

    // GPT-5 is now hidden by the filter, but its selection must still count.
    expect(screen.getByText('1 selected')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() =>
      expect(adminAiProvidersApi.applyModelSync).toHaveBeenCalledWith('provider-1', {
        added: [diffWithChanges.added[0]],
        removedFromVendor: [],
      }),
    )
  })

  it('Confirm is disabled with nothing selected, enables after a selection, and disables again once cleared', async () => {
    await openDiff()

    expect(screen.getByText('Confirm')).toBeDisabled()

    fireEvent.click(screen.getByLabelText('Select GPT-5'))
    expect(screen.getByText('Confirm')).not.toBeDisabled()

    fireEvent.click(screen.getByLabelText('Select GPT-5'))
    expect(screen.getByText('Confirm')).toBeDisabled()
  })

  it('renders a mixed applied/failed result, naming each failed model with its reason', async () => {
    vi.mocked(adminAiProvidersApi.applyModelSync).mockResolvedValue({
      appliedModelKeys: ['gpt-5'],
      failed: [{ modelKey: 'gpt-3.5', displayName: 'GPT-3.5', reason: "'gpt-3.5' already exists in the catalog — the diff is stale; re-run the sync check." }],
    })
    await openDiff()

    fireEvent.click(screen.getByLabelText('Select GPT-5'))
    fireEvent.click(screen.getByLabelText('Select GPT-3.5'))
    fireEvent.click(screen.getByText('Confirm'))

    expect(await screen.findByText(/applied 1 model/i)).toBeInTheDocument()
    expect(screen.getByText(/could not apply 1 model/i)).toBeInTheDocument()
    expect(screen.getByText('GPT-3.5')).toBeInTheDocument()
    expect(screen.getByText(/diff is stale/i)).toBeInTheDocument()
  })
})
