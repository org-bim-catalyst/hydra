import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
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
import type {
  AdminAiProvider,
  AiCapabilityAssignment,
  AiCapabilitySettings,
} from '../api/adminAiProvidersApi'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import { AdminAiCapabilitiesPage } from './AdminAiCapabilitiesPage'

expect.extend(toHaveNoViolations)

vi.mock('../api/adminAiProvidersApi', async () => {
  const actual = await vi.importActual<typeof adminAiProvidersApi>('../api/adminAiProvidersApi')
  return {
    ...actual,
    getProviders: vi.fn(),
    getCapabilityAssignments: vi.fn(),
    getCapabilitySettings: vi.fn(),
    updateCapabilitySettings: vi.fn(),
    setCapabilityAssignment: vi.fn(),
    getModels: vi.fn(),
  }
})

const openai: AdminAiProvider = {
  id: 'provider-openai',
  providerKey: 'openai',
  displayName: 'OpenAI',
  isEnabled: true,
  hasCredential: true,
  credentialHint: 'sk-p...33IA',
  credentialLastRotatedAtUtc: null,
  defaultModelId: 'model-gpt41',
  healthStatus: 'Healthy',
  healthStatusCheckedAtUtc: null,
  healthFailureKind: null,
  healthFailureReason: null,
  healthStaleAfterUtc: null,
  kind: 'Language',
}

const assignments: AiCapabilityAssignment[] = [
  {
    capability: 'LocationIntent',
    providerId: null,
    modelId: null,
    effectiveProviderId: 'provider-openai',
    effectiveModelId: 'model-gpt41',
  },
  {
    capability: 'BoundaryVision',
    providerId: null,
    modelId: null,
    effectiveProviderId: 'provider-openai',
    effectiveModelId: 'model-gpt41',
  },
]

const settings: AiCapabilitySettings[] = [
  {
    capability: 'BoundaryVision',
    settings: [
      {
        key: 'includeConnectedBuildings',
        valueType: 'Boolean',
        label: 'Include connected buildings of the same development',
        description: 'Proposes the towers and hotels joined to a mall as part of its site.',
        value: 'true',
        defaultValue: 'true',
      },
    ],
  },
]

beforeEach(() => {
  vi.clearAllMocks()
  setTheme()
})
afterEach(() => signOutAndResetTheme())

function renderPage(providers: AdminAiProvider[] = [openai]) {
  vi.mocked(adminAiProvidersApi.getProviders).mockResolvedValue(providers)
  vi.mocked(adminAiProvidersApi.getCapabilityAssignments).mockResolvedValue(assignments)
  vi.mocked(adminAiProvidersApi.getCapabilitySettings).mockResolvedValue(settings)
  vi.mocked(adminAiProvidersApi.getModels).mockResolvedValue([])
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <LocalizedSurface scope="subtree">
          <AdminAiCapabilitiesPage />
        </LocalizedSurface>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminAiCapabilitiesPage in Arabic (T215)', () => {
  it('renders in ar/rtl with Arabic copy and no fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderPage()

    expect(await screen.findByText('قدرات الذكاء الاصطناعي')).toBeInTheDocument()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(await screen.findByText('قصد الموقع')).toBeInTheDocument()
    expect(screen.getByText('رؤية حدود الموقع')).toBeInTheDocument()
    expect(screen.getByText(/يتطلب حاليًا Google Gemini/)).toBeInTheDocument()
    expect(screen.getByText('المزوّد المُسنَد')).toBeInTheDocument()
    expect(screen.getAllByText('يُرجى اختيار مزوّد الذكاء الاصطناعي').length).toBeGreaterThan(0)
    expect(await screen.findByRole('button', { name: 'إعدادات ⁨رؤية حدود الموقع⁩' })).toBeEnabled()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('warns in Arabic when no provider can be assigned', async () => {
    renderPage([])
    expect(await screen.findByText(/لا يمكن إسناد أي مزوّد بعد/)).toBeInTheDocument()
  })

  it('opens the settings dialog in Arabic, rtl, and shows server text as returned', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'إعدادات ⁨رؤية حدود الموقع⁩' }))

    // Open MUI dialogs break getByRole in jsdom, so plain text queries.
    expect(await screen.findByText('إعدادات ⁨رؤية حدود الموقع⁩')).toBeInTheDocument()
    expect(screen.getByText('إلغاء')).toBeInTheDocument()
    expect(screen.getByText('حفظ')).toBeInTheDocument()
    expect(
      screen.getByText('Include connected buildings of the same development'),
    ).toBeInTheDocument()
    expect(document.querySelector('.MuiDialog-root')).toHaveAttribute('dir', 'rtl')
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('surfaces a failed settings save as the server detail', async () => {
    const { ApiError } = await import('../../../api/httpClient')
    vi.mocked(adminAiProvidersApi.updateCapabilitySettings).mockRejectedValue(
      new ApiError(500, 'تعذّر', 'تعذّر حفظ الإعدادات.'),
    )
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'إعدادات ⁨رؤية حدود الموقع⁩' }))
    await screen.findByText('إلغاء')
    fireEvent.click(document.querySelector('input[type="checkbox"]') as HTMLElement)
    fireEvent.click(screen.getByText('حفظ'))
    await waitFor(() => expect(screen.getByText('تعذّر حفظ الإعدادات.')).toBeInTheDocument())
  })

  for (const mode of THEME_MODES) {
    it(`has no axe violations in ${mode} theme`, async () => {
      setTheme(mode)
      const { container } = renderPage()
      await screen.findByText('قصد الموقع')
      expect(await axe(container)).toHaveNoViolations()
    })
  }
})
