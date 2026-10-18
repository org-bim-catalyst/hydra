import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as deprecationsApi from '../api/modelDeprecationsApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { DeprecationAttentionBanner } from './DeprecationAttentionBanner'

vi.mock('../api/modelDeprecationsApi', async () => {
  const actual = await vi.importActual<typeof deprecationsApi>('../api/modelDeprecationsApi')
  return {
    ...actual,
    listModelDeprecations: vi.fn(),
    getModelDeprecation: vi.fn().mockResolvedValue(null),
    acceptModelReplacement: vi.fn(),
    changeModelReplacement: vi.fn(),
  }
})

vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return { ...actual, getModels: vi.fn().mockResolvedValue([]) }
})

const summary = {
  id: 'dep-1',
  modelId: 'model-old',
  modelKey: 'gpt-4-turbo',
  displayName: 'GPT-4 Turbo',
  providerId: 'provider-1',
  providerName: 'OpenAI',
  replacementModelId: 'model-new',
  replacementDisplayName: 'GPT-5',
  validationStatus: 'AwaitingValidation' as const,
  reassignedCount: 1,
  openFlagCount: 0,
  userImpactStatus: 'Completed' as const,
  notifiedUserCount: 2,
  confirmedAtUtc: '2026-10-18T09:00:00Z',
  confirmedByUserId: 'admin-1',
}

function renderBanner() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <DeprecationAttentionBanner />
    </QueryClientProvider>,
  )
}

describe('DeprecationAttentionBanner', () => {
  beforeEach(() => {
    vi.mocked(deprecationsApi.listModelDeprecations).mockResolvedValue({ items: [summary], nextCursor: null })
  })

  it('says how many replacements are awaiting validation', async () => {
    renderBanner()

    expect(await screen.findByText('1 deprecation(s) awaiting validation')).toBeTruthy()
  })

  it('opens the record when the administrator asks for it', async () => {
    renderBanner()

    fireEvent.click(await screen.findByText('Model deprecation'))

    // The button label and the dialog title share this text; two matches means the dialog opened.
    expect((await screen.findAllByText('Model deprecation')).length).toBeGreaterThan(1)
    expect(deprecationsApi.getModelDeprecation).toHaveBeenCalledWith('dep-1')
  })

  it('shows nothing when no replacement is awaiting validation', async () => {
    vi.mocked(deprecationsApi.listModelDeprecations).mockResolvedValue({ items: [], nextCursor: null })
    const { container } = renderBanner()

    await vi.waitFor(() => expect(container.textContent).toBe(''))
  })
})
