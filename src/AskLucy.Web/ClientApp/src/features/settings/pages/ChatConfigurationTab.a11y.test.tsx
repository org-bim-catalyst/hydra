import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import * as aiProvidersApi from '../../chat/api/aiProvidersApi'
import * as chatsApi from '../../chat/api/chatsApi'
import { useActiveConversationStore } from '../../chat/activeConversationStore'
import { ChatConfigurationTab } from './ChatConfigurationTab'

vi.mock('../../chat/api/chatsApi')
vi.mock('../../chat/api/aiProvidersApi')

expect.extend(toHaveNoViolations)

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
      <MemoryRouter>
        <ChatConfigurationTab />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('ChatConfigurationTab accessibility', () => {
  beforeEach(() => {
    vi.mocked(aiProvidersApi.getEnabledProviders).mockResolvedValue([PROVIDER])
    vi.mocked(aiProvidersApi.getModelsForProvider).mockResolvedValue([MODEL])
  })

  afterEach(() => {
    useActiveConversationStore.setState({ activeChatId: null })
  })

  it('has no automatically detectable a11y violations in the "no conversation open" state', async () => {
    useActiveConversationStore.setState({ activeChatId: null })
    const { container, findByText } = renderTab()

    await findByText('No conversation is currently open')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in the "conversation open" state', async () => {
    useActiveConversationStore.setState({ activeChatId: 'chat-1' })
    vi.mocked(chatsApi.getChatById).mockResolvedValue({
      id: 'chat-1',
      title: 'Test conversation',
      providerId: 'provider-1',
      modelId: 'model-1',
    })
    const { container, findByText } = renderTab()

    await findByText('GPT-4')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
