import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AdminAiModel, AdminAiProvider } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { ProviderModelsSection } from './ProviderModelsSection'

vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return { ...actual, getModels: vi.fn() }
})

const provider: AdminAiProvider = {
  id: 'provider-1',
  providerKey: 'openai',
  displayName: 'OpenAI',
  isEnabled: true,
  hasCredential: true,
  credentialLastRotatedAtUtc: null,
  defaultModelId: null,
  healthStatus: 'Healthy',
  healthStatusCheckedAtUtc: null,
  healthFailureKind: null,
  healthFailureReason: null,
  healthStaleAfterUtc: null,
}

const baseModel: AdminAiModel = {
  id: 'model-1',
  modelKey: 'gpt-4-turbo',
  displayName: 'gpt-4-turbo',
  contextWindowTokens: null,
  maxOutputTokens: null,
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
  status: 'Unavailable',
}

function renderSection(models: AdminAiModel[]) {
  vi.mocked(adminAiProvidersApi.getModels).mockResolvedValue(models)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ProviderModelsSection provider={provider} />
    </QueryClientProvider>,
  )
}

describe('ProviderModelsSection token limits (specs/043 US4)', () => {
  it('shows an unpublished token limit as not published, never as zero (FR-030)', async () => {
    renderSection([baseModel])

    await waitFor(() => expect(screen.getByText('Not published by the vendor')).toBeInTheDocument())
    expect(screen.queryByText(/\b0 in\b/)).not.toBeInTheDocument()
  })

  it('does not reuse the word "unknown" for token limits (FR-029a)', async () => {
    // "Unknown" already means two other things on this page — the provider health status, and
    // absent pricing in this very table. Reusing it here would collapse three unrelated
    // conditions into one label.
    renderSection([baseModel])

    await waitFor(() => expect(screen.getByText('Not published by the vendor')).toBeInTheDocument())

    const limitsCell = screen.getByText('Not published by the vendor')
    expect(limitsCell.textContent).not.toMatch(/unknown/i)
  })

  it('shows published figures as published', async () => {
    renderSection([{ ...baseModel, contextWindowTokens: 128_000, maxOutputTokens: 16_384 }])

    await waitFor(() => expect(screen.getByText(/128,000 in \/ 16,384 out/)).toBeInTheDocument())
  })

  it('handles a model with only one figure published', async () => {
    // OpenRouter publishes a context length but no output limit.
    renderSection([{ ...baseModel, contextWindowTokens: 64_000, maxOutputTokens: null }])

    await waitFor(() => expect(screen.getByText(/64,000 in \/ Not published by the vendor out/)).toBeInTheDocument())
  })

  it('still shows absent pricing as Unknown, which this feature does not change', async () => {
    renderSection([baseModel])

    await waitFor(() => expect(screen.getByText('Unknown')).toBeInTheDocument())
  })
})
