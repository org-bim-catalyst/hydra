import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AdminAiModel, AdminAiProvider } from '../api/adminAiProvidersApi'
import { AdminAiProvidersPage } from './AdminAiProvidersPage'

expect.extend(toHaveNoViolations)

const providers: AdminAiProvider[] = [
  {
    id: 'provider-1',
    providerKey: 'openai',
    displayName: 'OpenAI',
    isEnabled: true,
    hasCredential: true,
    credentialLastRotatedAtUtc: '2026-07-30T00:00:00Z',
    defaultModelId: null,
    healthStatus: 'Healthy',
    healthStatusCheckedAtUtc: '2026-07-31T00:00:00Z',
    healthFailureKind: null,
    healthFailureReason: null,
    healthStaleAfterUtc: null,
    isEffectivePlatformDefault: false,
  },
  {
    id: 'provider-2',
    providerKey: 'anthropic',
    displayName: 'Anthropic',
    isEnabled: false,
    hasCredential: false,
    credentialLastRotatedAtUtc: null,
    defaultModelId: null,
    healthStatus: 'Unknown',
    healthStatusCheckedAtUtc: null,
    healthFailureKind: null,
    healthFailureReason: null,
    healthStaleAfterUtc: null,
    isEffectivePlatformDefault: false,
  },
  {
    // specs/043 US2 — a provider that is configured and credentialled but temporarily limited,
    // whose last result is also past its freshness horizon. Exercises both the warning-state
    // chip and the staleness affordance under axe.
    id: 'provider-3',
    providerKey: 'google-gemini',
    displayName: 'Google Gemini',
    isEnabled: true,
    hasCredential: true,
    credentialLastRotatedAtUtc: '2026-07-30T00:00:00Z',
    defaultModelId: null,
    healthStatus: 'Unhealthy',
    healthStatusCheckedAtUtc: '2026-07-31T00:00:00Z',
    healthFailureKind: 'QuotaExhausted',
    healthFailureReason: 'Google Gemini is configured correctly, but its usage quota is exhausted.',
    healthStaleAfterUtc: '2026-07-31T00:06:00Z',
    isEffectivePlatformDefault: false,
  },
  {
    // A credential failure, so the error-state chip and its reason are covered too.
    id: 'provider-4',
    providerKey: 'openrouter',
    displayName: 'OpenRouter',
    isEnabled: true,
    hasCredential: true,
    credentialLastRotatedAtUtc: '2026-07-30T00:00:00Z',
    defaultModelId: null,
    healthStatus: 'Unhealthy',
    healthStatusCheckedAtUtc: '2026-07-31T00:00:00Z',
    healthFailureKind: 'CredentialRejected',
    healthFailureReason: 'OpenRouter rejected the configured credential.',
    healthStaleAfterUtc: null,
    isEffectivePlatformDefault: false,
  },
]

const models: AdminAiModel[] = [
  {
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
    pricing: { inputPerMillionTokensUsd: 2.5, outputPerMillionTokensUsd: 10 },
    releaseDate: '2026-01-01',
    status: 'Available',
  },
]

const syncDiff = {
  added: [
    {
      modelKey: 'gpt-5',
      displayName: 'GPT-5',
      contextWindowTokens: 200000,
      maxOutputTokens: 32000,
      capabilities: models[0].capabilities,
    },
  ],
  removedFromVendor: [],
}

const server = setupServer(
  http.get('*/api/v1/admin/ai/providers', () => HttpResponse.json(providers)),
  http.get('*/api/v1/admin/ai/providers/:providerId/models', () => HttpResponse.json(models)),
  http.post('*/api/v1/admin/ai/providers/:providerId/models/actions/sync', () => HttpResponse.json(syncDiff)),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminAiProvidersPage accessibility', () => {
  it('has no automatically detectable a11y violations (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminAiProvidersPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('OpenAI')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with a provider row expanded — model table, status menu, and sync dialog open (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText, findByRole, getByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminAiProvidersPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('OpenAI')
    fireEvent.click(await findByRole('button', { name: /expand models for openai/i }))
    await findByText('GPT-4.1')

    fireEvent.click(getByText('Sync from provider'))
    await findByText('Check for updates')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with the sync diff filtered and a row checked (constitution §10, specs/009-selective-model-sync-review)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container, findByText, findByRole, getByText, getByLabelText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminAiProvidersPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await findByText('OpenAI')
    fireEvent.click(await findByRole('button', { name: /expand models for openai/i }))
    await findByText('GPT-4.1')

    fireEvent.click(getByText('Sync from provider'))
    fireEvent.click(await findByText('Check for updates'))
    await findByText('GPT-5')

    fireEvent.change(getByLabelText('Filter by name or key'), { target: { value: 'gpt' } })
    fireEvent.click(getByLabelText('Select GPT-5'))

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
