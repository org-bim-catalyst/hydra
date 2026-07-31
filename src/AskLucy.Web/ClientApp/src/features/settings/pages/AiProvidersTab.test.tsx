import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as aiPreferencesApi from '../api/aiPreferencesApi'
import * as aiProvidersApi from '../../chat/api/aiProvidersApi'
import { AiProvidersTab } from './SettingsPage'

vi.mock('../api/aiPreferencesApi')
vi.mock('../../chat/api/aiProvidersApi')

const PROVIDER: aiProvidersApi.ProviderSummary = {
  id: 'provider-1',
  providerKey: 'openai',
  displayName: 'OpenAI',
  healthStatus: 'Healthy',
  healthStatusCheckedAtUtc: null,
}

const MODEL: aiProvidersApi.ModelSummary = {
  id: 'model-1',
  modelKey: 'gpt-4',
  displayName: 'GPT-4',
  contextWindowTokens: 128000,
  maxOutputTokens: 4096,
  capabilities: {
    streaming: true,
    vision: false,
    functionCalling: false,
    jsonMode: false,
    reasoning: false,
    embeddings: false,
    imageInput: false,
    imageOutput: false,
    audio: false,
  },
  pricing: null,
  releaseDate: null,
  providerId: 'provider-1',
  providerDisplayName: 'OpenAI',
}

function renderTab() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <AiProvidersTab />
    </QueryClientProvider>,
  )
}

describe('AiProvidersTab', () => {
  beforeEach(() => {
    vi.mocked(aiProvidersApi.getEnabledProviders).mockResolvedValue([PROVIDER])
    vi.mocked(aiProvidersApi.getModelsForProvider).mockResolvedValue([MODEL])
  })

  it('shows the platform-default notice when the caller has no saved preference', async () => {
    vi.mocked(aiPreferencesApi.getPreferences).mockResolvedValue({
      defaultProviderId: 'provider-1',
      defaultModelId: 'model-1',
      defaultGenerationParameters: null,
      isPlatformDefault: true,
    })
    renderTab()

    expect(await screen.findByText(/haven't saved a personal default yet/)).toBeInTheDocument()
  })

  it('does not show the platform-default notice once the user has saved their own preference', async () => {
    vi.mocked(aiPreferencesApi.getPreferences).mockResolvedValue({
      defaultProviderId: 'provider-1',
      defaultModelId: 'model-1',
      defaultGenerationParameters: null,
      isPlatformDefault: false,
    })
    renderTab()

    await screen.findByText('OpenAI')
    expect(screen.queryByText(/haven't saved a personal default yet/)).not.toBeInTheDocument()
  })

  it('saves the selected provider/model when Save default is clicked', async () => {
    vi.mocked(aiPreferencesApi.getPreferences).mockResolvedValue({
      defaultProviderId: 'provider-1',
      defaultModelId: 'model-1',
      defaultGenerationParameters: null,
      isPlatformDefault: true,
    })
    vi.mocked(aiPreferencesApi.savePreferences).mockResolvedValue({
      defaultProviderId: 'provider-1',
      defaultModelId: 'model-1',
      defaultGenerationParameters: null,
      isPlatformDefault: false,
    })
    renderTab()

    const saveButton = await screen.findByRole('button', { name: 'Save default' })
    await waitFor(() => expect(saveButton).toBeEnabled())
    saveButton.click()

    await waitFor(() =>
      expect(aiPreferencesApi.savePreferences).toHaveBeenCalledWith('provider-1', 'model-1', undefined),
    )
    expect(await screen.findByText('Default saved.')).toBeInTheDocument()
  })
})
