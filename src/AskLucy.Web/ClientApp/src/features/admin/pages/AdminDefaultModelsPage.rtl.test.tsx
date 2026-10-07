import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocalizedSurface } from '../../../i18n/LocalizedSurface'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { AdminAiModel, AdminAiProvider } from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { AdminDefaultModelsPage } from './AdminDefaultModelsPage'

expect.extend(toHaveNoViolations)

vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return { ...actual, getProviders: vi.fn(), getModels: vi.fn(), updateProvider: vi.fn() }
})

const openai: AdminAiProvider = {
  id: 'provider-openai',
  providerKey: 'openai',
  displayName: 'OpenAI',
  isEnabled: true,
  hasCredential: true,
  credentialHint: 'sk-p...33IA',
  credentialLastRotatedAtUtc: null,
  defaultModelId: null,
  healthStatus: 'Healthy',
  healthStatusCheckedAtUtc: null,
  healthFailureKind: null,
  healthFailureReason: null,
  healthStaleAfterUtc: null,
  kind: 'Language',
}

const gpt: AdminAiModel = {
  id: 'model-gpt41',
  modelKey: 'gpt-4.1',
  displayName: 'GPT-4.1',
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
  status: 'Available',
}

beforeEach(() => {
  vi.clearAllMocks()
  setTheme()
})
afterEach(() => signOutAndResetTheme())

function renderPage(providers: AdminAiProvider[], models: AdminAiModel[]) {
  vi.mocked(adminAiProvidersApi.getProviders).mockResolvedValue(providers)
  vi.mocked(adminAiProvidersApi.getModels).mockResolvedValue(models)
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <LocalizedSurface scope="subtree">
          <AdminDefaultModelsPage />
        </LocalizedSurface>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminDefaultModelsPage in Arabic (T215)', () => {
  it('renders in ar/rtl with Arabic copy, verbatim provider names and no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderPage([openai], [gpt])

    expect(await screen.findByText('النماذج الافتراضية')).toBeInTheDocument()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('المزوّد')).toBeInTheDocument()
    expect(await screen.findByText('OpenAI')).toBeInTheDocument()
    expect(screen.getByText('مفعّل')).toBeInTheDocument()
    await waitFor(() =>
      expect(
        document.querySelector(
          '[role="combobox"][aria-labelledby="provider-openai-default-model-label"]',
        ),
      ).not.toBeNull(),
    )
    expect(document.getElementById('provider-openai-default-model-label')).toHaveTextContent(
      'النموذج الافتراضي لـ ⁨OpenAI⁩',
    )
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the Arabic empty state and the explanatory note', async () => {
    renderPage([], [])
    expect(await screen.findByText('لا يوجد مزوّدون قابلون للإسناد.')).toBeInTheDocument()
    expect(screen.getByText(/لا يظهر هنا إلا المزوّدون المفعّلون/)).toBeInTheDocument()
  })

  it('explains a provider with no Available model in Arabic', async () => {
    renderPage([openai], [])
    expect(
      await screen.findByText('لا توجد نماذج متاحة — علّم نموذجًا كمتاح في صفحة المزوّدين أولًا.'),
    ).toBeInTheDocument()
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage([openai], [gpt])
      await screen.findByText('OpenAI')
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})
