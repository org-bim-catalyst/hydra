import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AdminAiModel } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { AiModelStatusMenu } from './AiModelStatusMenu'

// Same reasoning as AiProviderActionsMenu.test.tsx: MUI's Paper-based Popper/Dialog
// surfaces render an inline `--Paper-shadow` custom property jsdom's CSS length parser
// cannot resolve, which crashes role-based a11y checks on children — use fireEvent +
// text/DOM queries instead of getByRole for anything inside an open Dialog.
vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return {
    ...actual,
    updateModelStatus: vi.fn().mockResolvedValue(undefined),
  }
})

const baseModel: AdminAiModel = {
  id: 'model-1',
  modelKey: 'gpt-4.1',
  displayName: 'GPT-4.1',
  contextWindowTokens: 128000,
  maxOutputTokens: 16384,
  capabilities: {
    streaming: true,
    vision: true,
    functionCalling: true,
    jsonMode: true,
    reasoning: false,
    embeddings: false,
    imageInput: true,
    imageOutput: false,
    audio: false,
  },
  pricing: null,
  releaseDate: null,
  status: 'Available',
}

function renderMenu(model: AdminAiModel) {
  const queryClient = new QueryClient()
  return render(
    <QueryClientProvider client={queryClient}>
      <AiModelStatusMenu model={model} providerId="provider-1" />
    </QueryClientProvider>,
  )
}

describe('AiModelStatusMenu', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows a "Mark unavailable" toggle for an Available model; Cancel does not call the API', async () => {
    renderMenu(baseModel)

    expect(screen.getByRole('button', { name: /mark unavailable for gpt-4.1/i })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /mark unavailable for gpt-4.1/i }))
    expect(await screen.findByText('Mark this model Unavailable?')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Cancel'))

    expect(adminAiProvidersApi.updateModelStatus).not.toHaveBeenCalled()
  })

  it('calls updateModelStatus with Unavailable only on Confirm', async () => {
    renderMenu(baseModel)

    fireEvent.click(screen.getByRole('button', { name: /mark unavailable for gpt-4.1/i }))
    expect(adminAiProvidersApi.updateModelStatus).not.toHaveBeenCalled()

    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminAiProvidersApi.updateModelStatus).toHaveBeenCalledWith('model-1', 'Unavailable'))
  })

  it('disables the toggle for a Deprecated model', () => {
    renderMenu({ ...baseModel, status: 'Deprecated' })

    const toggle = screen.getByRole('button', {
      name: /deprecated by the vendor — cannot be re-enabled from here for gpt-4.1/i,
    })
    expect(toggle).toBeDisabled()

    fireEvent.click(toggle)
    expect(adminAiProvidersApi.updateModelStatus).not.toHaveBeenCalled()
  })

  it('shows a "Mark available" toggle for an Unavailable model', () => {
    renderMenu({ ...baseModel, status: 'Unavailable' })

    expect(screen.getByRole('button', { name: /mark available for gpt-4.1/i })).toBeInTheDocument()
  })
})
