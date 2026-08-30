import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AdminAiModel, AdminAiProvider } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { ProviderModelsSection } from './ProviderModelsSection'

vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return { ...actual, getModels: vi.fn(), updateProvider: vi.fn() }
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
  isEffectivePlatformDefault: false,
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

function renderSection(models: AdminAiModel[], providerOverride: Partial<AdminAiProvider> = {}) {
  vi.mocked(adminAiProvidersApi.getModels).mockResolvedValue(models)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ProviderModelsSection provider={{ ...provider, ...providerOverride }} />
    </QueryClientProvider>,
  )
}

const availableModel: AdminAiModel = { ...baseModel, status: 'Available' }

beforeEach(() => vi.clearAllMocks())

/**
 * The control that did not exist until now. The PATCH endpoint always accepted
 * `defaultModelId`, but nothing in the UI ever sent it, so every provider sat at null and
 * DefaultProviderResolver fell through to "first enabled provider in display-name order" —
 * which routed location intent classification to a provider the operator never chose.
 */
describe('ProviderModelsSection platform default model', () => {
  it('sets the provider default from an Available model', async () => {
    vi.mocked(adminAiProvidersApi.updateProvider).mockResolvedValue(undefined)
    renderSection([availableModel])

    const radio = await screen.findByLabelText(/Set gpt-4-turbo as the default model for OpenAI/)
    fireEvent.click(radio)

    await waitFor(() =>
      expect(adminAiProvidersApi.updateProvider).toHaveBeenCalledWith('provider-1', {
        defaultModelId: 'model-1',
      }),
    )
  })

  it('refuses to offer a non-Available model, which the resolver would silently skip', async () => {
    renderSection([baseModel]) // status: 'Unavailable'

    const radio = await screen.findByLabelText(/Set gpt-4-turbo as the default model for OpenAI/)
    expect(radio).toBeDisabled()
  })

  it('clears via an explicit flag, never by sending a null id', async () => {
    // Server-side, a null defaultModelId means "leave it alone" so that a PATCH toggling
    // isEnabled cannot wipe the default as a side effect. Clearing needs its own signal.
    vi.mocked(adminAiProvidersApi.updateProvider).mockResolvedValue(undefined)
    renderSection([availableModel], { defaultModelId: 'model-1' })

    fireEvent.click(await screen.findByRole('button', { name: 'Clear default' }))

    await waitFor(() =>
      expect(adminAiProvidersApi.updateProvider).toHaveBeenCalledWith('provider-1', {
        clearDefaultModel: true,
      }),
    )
  })

  it('surfaces a failed update to the user rather than only the console', async () => {
    vi.mocked(adminAiProvidersApi.updateProvider).mockRejectedValue(new Error('boom'))
    renderSection([availableModel])

    fireEvent.click(await screen.findByLabelText(/Set gpt-4-turbo as the default model for OpenAI/))

    expect(await screen.findByText(/Something went wrong/)).toBeInTheDocument()
  })

  it('warns that setting a default does not guarantee this provider wins', async () => {
    // The rule is alphabetical-first-with-a-default. Setting one here while an earlier provider
    // also has one changes nothing, which is exactly how the original accident happened.
    renderSection([availableModel], { isEffectivePlatformDefault: false })

    expect(await screen.findByText(/first enabled provider, in alphabetical order/)).toBeInTheDocument()
  })

  it('confirms plainly when this provider is the one actually serving', async () => {
    renderSection([availableModel], { isEffectivePlatformDefault: true })

    expect(await screen.findByText(/is currently the platform default/)).toBeInTheDocument()
  })
})

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
