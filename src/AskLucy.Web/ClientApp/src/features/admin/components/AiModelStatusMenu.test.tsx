import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AdminAiModel } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { AiModelStatusMenu } from './AiModelStatusMenu'

// Same reasoning as AiProviderActionsMenu.test.tsx: MUI's Paper-based Popper/Dialog
// surfaces render an inline `--Paper-shadow` custom property jsdom's CSS length parser
// cannot resolve, which crashes role-based a11y checks on children — use fireEvent +
// text/DOM queries instead of getByRole for anything inside a Menu/Dialog.
vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return {
    ...actual,
    updateModelStatus: vi.fn().mockResolvedValue(undefined),
  }
})

const availableModel: AdminAiModel = {
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

  it('opens a confirm dialog for each target status; Cancel does not call the API', async () => {
    renderMenu(availableModel)

    fireEvent.click(screen.getByRole('button', { name: /change status for gpt-4.1/i }))
    fireEvent.click(await screen.findByText('Mark Deprecated'))

    expect(await screen.findByText('Mark this model Deprecated?')).toBeInTheDocument()
    fireEvent.click(screen.getByText('Cancel'))

    expect(adminAiProvidersApi.updateModelStatus).not.toHaveBeenCalled()
  })

  it('calls updateModelStatus only on Confirm', async () => {
    renderMenu(availableModel)

    fireEvent.click(screen.getByRole('button', { name: /change status for gpt-4.1/i }))
    fireEvent.click(await screen.findByText('Mark Unavailable'))
    expect(adminAiProvidersApi.updateModelStatus).not.toHaveBeenCalled()

    fireEvent.click(screen.getByText('Confirm'))

    await waitFor(() => expect(adminAiProvidersApi.updateModelStatus).toHaveBeenCalledWith('model-1', 'Unavailable'))
  })

  it('does not offer the model\'s current status as a menu option', async () => {
    renderMenu(availableModel)

    fireEvent.click(screen.getByRole('button', { name: /change status for gpt-4.1/i }))

    expect(await screen.findByText('Mark Deprecated')).toBeInTheDocument()
    expect(screen.queryByText('Mark Available')).not.toBeInTheDocument()
  })
})
