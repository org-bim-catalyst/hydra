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
    credentialHint: 'sk-p...33IA',
    credentialLastRotatedAtUtc: '2026-07-30T00:00:00Z',
    defaultModelId: null,
    healthStatus: 'Healthy',
    healthStatusCheckedAtUtc: '2026-07-31T00:00:00Z',
    healthFailureKind: null,
    healthFailureReason: null,
    healthStaleAfterUtc: null,
    kind: 'Language',
  },
  {
    id: 'provider-2',
    providerKey: 'anthropic',
    displayName: 'Anthropic',
    isEnabled: false,
    hasCredential: false,
    credentialHint: null,
    credentialLastRotatedAtUtc: null,
    defaultModelId: null,
    healthStatus: 'Unknown',
    healthStatusCheckedAtUtc: null,
    healthFailureKind: null,
    healthFailureReason: null,
    healthStaleAfterUtc: null,
    kind: 'Language',
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
    credentialHint: 'sk-a...ygAA',
    credentialLastRotatedAtUtc: '2026-07-30T00:00:00Z',
    defaultModelId: null,
    healthStatus: 'Unhealthy',
    healthStatusCheckedAtUtc: '2026-07-31T00:00:00Z',
    healthFailureKind: 'QuotaExhausted',
    healthFailureReason: 'Google Gemini is configured correctly, but its usage quota is exhausted.',
    healthStaleAfterUtc: '2026-07-31T00:06:00Z',
    kind: 'Language',
  },
  {
    // A credential failure, so the error-state chip and its reason are covered too.
    id: 'provider-4',
    providerKey: 'openrouter',
    displayName: 'OpenRouter',
    isEnabled: true,
    hasCredential: true,
    credentialHint: 'sk-o...ROUT',
    credentialLastRotatedAtUtc: '2026-07-30T00:00:00Z',
    defaultModelId: null,
    healthStatus: 'Unhealthy',
    healthStatusCheckedAtUtc: '2026-07-31T00:00:00Z',
    healthFailureKind: 'CredentialRejected',
    healthFailureReason: 'OpenRouter rejected the configured credential.',
    healthStaleAfterUtc: null,
    kind: 'Language',
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
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: [],
      permissions: ['admin.ai-providers.view', 'admin.ai-providers.manage', 'admin.custom-models.view'],
    }),
  ),
  http.get('*/api/v1/admin/custom-models/deployment-status', () =>
    HttpResponse.json({ isConfigured: true, transport: 'FTPS', maxDeploymentBytes: 2 ** 30 }),
  ),
  http.get('*/api/v1/admin/custom-models', () =>
    HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
  ),
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

  it('renders the staleness indicator in its own column, never sharing a cell with the health chip (specs/062 FR-001/FR-002/SC-001)', async () => {
    // Regression coverage for the original bug: the staleness chip used to share one
    // flex-wrap cell with the health status chip, causing it to wrap onto a second line and
    // its tooltip to overlap the row below at narrow viewport widths. Asserting the two chips
    // live in different table cells structurally guarantees no shared container remains for
    // that wrap to happen in, independent of any single viewport width.
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { findByText } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminAiProvidersPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    const healthChip = await findByText('Configured — temporarily limited')
    const stalenessChip = await findByText('Possibly out of date')

    const healthCell = healthChip.closest('td')
    const stalenessCell = stalenessChip.closest('td')

    expect(healthCell).not.toBeNull()
    expect(stalenessCell).not.toBeNull()
    expect(healthCell).not.toBe(stalenessCell)
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
    await findByText(/fetching models/i)

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
    await findByText('GPT-5')

    fireEvent.change(getByLabelText('Filter by name or key'), { target: { value: 'gpt' } })
    fireEvent.click(getByLabelText('Select GPT-5'))

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
