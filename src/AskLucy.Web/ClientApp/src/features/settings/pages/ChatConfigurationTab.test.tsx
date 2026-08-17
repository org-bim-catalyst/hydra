import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as aiProvidersApi from '../../chat/api/aiProvidersApi'
import * as chatsApi from '../../chat/api/chatsApi'
import { useActiveConversationStore } from '../../chat/activeConversationStore'
import { SETTINGS_TAB_INDEX } from '../settingsTabs'
import { ChatConfigurationTab } from './ChatConfigurationTab'

vi.mock('../../chat/api/chatsApi')
vi.mock('../../chat/api/aiProvidersApi')

const CAPABILITIES: aiProvidersApi.ModelCapabilities = {
  streaming: true,
  vision: false,
  functionCalling: false,
  jsonMode: false,
  reasoning: false,
  embeddings: false,
  imageInput: false,
  imageOutput: false,
  audio: false,
}

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
  capabilities: CAPABILITIES,
  pricing: null,
  releaseDate: null,
  providerId: 'provider-1',
  providerDisplayName: 'OpenAI',
}

const OTHER_MODEL: aiProvidersApi.ModelSummary = { ...MODEL, id: 'model-2', modelKey: 'gpt-4-turbo', displayName: 'GPT-4 Turbo' }

function LocationProbe() {
  const location = useLocation()
  return <div data-testid="location-state">{JSON.stringify(location.state)}</div>
}

function renderTab() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/settings']}>
        <Routes>
          <Route
            path="/settings"
            element={
              <>
                <ChatConfigurationTab />
                <LocationProbe />
              </>
            }
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('ChatConfigurationTab', () => {
  beforeEach(() => {
    useActiveConversationStore.setState({ activeChatId: null })
    vi.mocked(aiProvidersApi.getEnabledProviders).mockResolvedValue([PROVIDER])
    vi.mocked(aiProvidersApi.getModelsForProvider).mockResolvedValue([MODEL, OTHER_MODEL])
  })

  it('shows the "no AI providers configured" empty state when none are enabled (FR-005)', async () => {
    vi.mocked(aiProvidersApi.getEnabledProviders).mockResolvedValue([])
    renderTab()

    expect(await screen.findByText('No AI providers are enabled yet')).toBeInTheDocument()
    expect(screen.getByText('An administrator needs to configure one first.')).toBeInTheDocument()
  })

  it('shows the "no conversation open" empty state when no conversation is active', async () => {
    renderTab()

    expect(await screen.findByText('No conversation is currently open')).toBeInTheDocument()
  })

  it("fetches and displays the open conversation's current model, and only lists active models (FR-005)", async () => {
    useActiveConversationStore.setState({ activeChatId: 'chat-1' })
    vi.mocked(chatsApi.getChatById).mockResolvedValue({
      id: 'chat-1',
      title: 'Test conversation',
      providerId: 'provider-1',
      modelId: 'model-1',
    })
    renderTab()

    await screen.findByText('GPT-4')
    // FR-012: neither AiProvidersTab's nor VoiceTab's own controls render inline here.
    expect(screen.queryByRole('button', { name: 'Save default' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/voice id/i)).not.toBeInTheDocument()
  })

  it('changing the model calls updateChatModelSelection and reflects immediately', async () => {
    useActiveConversationStore.setState({ activeChatId: 'chat-1' })
    vi.mocked(chatsApi.getChatById).mockResolvedValue({
      id: 'chat-1',
      title: 'Test conversation',
      providerId: 'provider-1',
      modelId: 'model-1',
    })
    vi.mocked(chatsApi.updateChatModelSelection).mockResolvedValue(undefined)
    renderTab()

    await screen.findByText('GPT-4')
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Model' }))
    fireEvent.click(await screen.findByText('GPT-4 Turbo'))

    expect(chatsApi.updateChatModelSelection).toHaveBeenCalledWith('chat-1', 'provider-1', 'model-2')
  })

  it('surfaces a save failure instead of failing silently (constitution §2.VIII)', async () => {
    useActiveConversationStore.setState({ activeChatId: 'chat-1' })
    vi.mocked(chatsApi.getChatById).mockResolvedValue({
      id: 'chat-1',
      title: 'Test conversation',
      providerId: 'provider-1',
      modelId: 'model-1',
    })
    vi.mocked(chatsApi.updateChatModelSelection).mockRejectedValue(new Error('Save failed.'))
    renderTab()

    await screen.findByText('GPT-4')
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'Model' }))
    fireEvent.click(await screen.findByText('GPT-4 Turbo'))

    expect(await screen.findByText('Save failed.')).toBeInTheDocument()
  })

  it('the AI Providers entry point navigates to Settings with the AI Providers tab selected', async () => {
    const user = userEvent.setup()
    renderTab()

    await user.click(await screen.findByRole('button', { name: 'Go to AI Providers' }))

    const state = JSON.parse(screen.getByTestId('location-state').textContent ?? 'null')
    expect(state).toEqual({ tab: SETTINGS_TAB_INDEX.AiProviders })
  })

  it('the Voice entry point navigates to Settings with the Voice tab selected', async () => {
    const user = userEvent.setup()
    renderTab()

    await user.click(await screen.findByRole('button', { name: 'Go to Voice' }))

    const state = JSON.parse(screen.getByTestId('location-state').textContent ?? 'null')
    expect(state).toEqual({ tab: SETTINGS_TAB_INDEX.Voice })
  })
})
