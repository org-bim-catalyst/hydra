import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest'
import {
  seedArabic,
  setTheme,
  signOutAndResetTheme,
  THEME_MODES,
  watchI18nFallbacks,
} from '../../../i18n/testUtils'
import type { AdminAiModel, AdminAiProvider } from '../api/adminAiProvidersApi'
import { customModel } from '../components/customModels/customModelFixtures'
import { AdminAiProvidersPage } from './AdminAiProvidersPage'

expect.extend(toHaveNoViolations)

const base: AdminAiProvider = {
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
}

const providers: AdminAiProvider[] = [
  base,
  {
    ...base,
    id: 'provider-2',
    providerKey: 'anthropic',
    displayName: 'Anthropic',
    isEnabled: false,
    hasCredential: false,
    credentialHint: null,
    healthStatus: 'Unknown',
    healthStatusCheckedAtUtc: null,
  },
  {
    ...base,
    id: 'provider-3',
    providerKey: 'google-gemini',
    displayName: 'Google Gemini',
    healthStatus: 'Unhealthy',
    healthFailureKind: 'QuotaExhausted',
    healthFailureReason: 'تم استنفاد حصة الاستخدام.',
    healthStaleAfterUtc: '2026-07-31T00:06:00Z',
  },
]

const models: AdminAiModel[] = [
  {
    id: 'model-1',
    modelKey: 'gpt-4.1',
    displayName: 'GPT-4.1',
    contextWindowTokens: 128000,
    maxOutputTokens: null,
    capabilities: {
      streaming: true,
      vision: true,
      functionCalling: false,
      jsonMode: false,
      reasoning: false,
      embeddings: false,
      imageInput: false,
      imageOutput: false,
      audio: false,
    },
    pricing: { inputPerMillionTokensUsd: 2.5, outputPerMillionTokensUsd: 10 },
    releaseDate: null,
    status: 'Available',
  },
]

const syncDiff = {
  added: [
    {
      modelKey: 'gpt-5',
      displayName: 'GPT-5',
      contextWindowTokens: null,
      maxOutputTokens: null,
      capabilities: models[0].capabilities,
    },
  ],
  removedFromVendor: [{ id: 'model-9', modelKey: 'gpt-3.5', displayName: 'GPT-3.5' }],
}

const failedModel = customModel({
  id: 'model-f',
  name: 'broken-model',
  deploymentState: 'Failed',
  availability: 'Unavailable',
  canMakeAvailable: false,
  availabilityBlockedReason: 'لم يكتمل النشر.',
  failureKind: 'TargetConnectionLost',
  failureReason: 'انقطع الاتصال بوجهة النشر.',
  overwrittenFileCount: 2,
  canRemove: true,
  totalBytes: 1536 * 2 ** 20,
})

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({
      authenticated: true,
      userId: 'admin-1',
      roles: [],
      permissions: [
        'admin.ai-providers.view',
        'admin.ai-providers.manage',
        'admin.custom-models.view',
        'admin.custom-models.manage',
      ],
    }),
  ),
  http.get('*/api/v1/admin/ai/providers', () => HttpResponse.json(providers)),
  http.get('*/api/v1/admin/ai/providers/:providerId/models', () => HttpResponse.json(models)),
  http.post('*/api/v1/admin/ai/providers/:providerId/models/actions/sync', () =>
    HttpResponse.json(syncDiff),
  ),
  http.get('*/api/v1/admin/custom-models/deployment-status', () =>
    HttpResponse.json({ isConfigured: true, transport: 'FTP', maxDeploymentBytes: 2 ** 30 }),
  ),
  http.get('*/api/v1/admin/custom-models', () =>
    HttpResponse.json({ items: [failedModel], page: 1, pageSize: 50, totalCount: 1 }),
  ),
)

beforeAll(() => server.listen())
beforeEach(() => setTheme())
afterEach(() => {
  server.resetHandlers()
  signOutAndResetTheme()
})
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  seedArabic(queryClient)
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/admin/ai-providers']}>
        <AdminAiProvidersPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminAiProvidersPage in Arabic (T215)', () => {
  it('renders in ar/rtl with Arabic copy, verbatim provider names and no catalog fallbacks', async () => {
    const fallbacks = watchI18nFallbacks()
    const { container } = renderPage()

    expect(await screen.findByText('OpenAI')).toBeInTheDocument()
    expect(container.querySelector('div[lang="ar"]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getAllByText('مزوّدو الذكاء الاصطناعي').length).toBeGreaterThan(0)
    expect(screen.getByText('النماذج المتقدمة')).toBeInTheDocument()
    expect(screen.getByText('الحالة الصحية')).toBeInTheDocument()
    expect(screen.getByText('Anthropic')).toBeInTheDocument()
    expect(screen.getByText('Google Gemini')).toBeInTheDocument()
    // The unconfigured provider shows the state in both the credential and the health column.
    expect(screen.getAllByText('غير مهيّأ')).toHaveLength(2)
    // A quota-limited provider reads as limited (not broken), with the server's own reason and the stale marker.
    expect(screen.getByText('مهيّأ — محدود مؤقتًا')).toBeInTheDocument()
    expect(screen.getByText('تم استنفاد حصة الاستخدام.')).toBeInTheDocument()
    expect(screen.getByText('قد يكون قديمًا')).toBeInTheDocument()
    expect(screen.getByText('غير محدد')).toBeInTheDocument()
    // Credential hint is an identifier: shown left-to-right.
    expect(screen.getAllByText('sk-p...33IA')[0]).toHaveAttribute('dir', 'ltr')
    expect(
      screen.getByRole('button', { name: /^استبدال بيانات الاعتماد لـ ⁨OpenAI⁩$/ }),
    ).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('shows the Arabic model table with identifiers, pricing and capabilities', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: /^توسيع نماذج ⁨OpenAI⁩$/ }))

    expect(await screen.findByText('GPT-4.1')).toBeInTheDocument()
    expect(screen.getByText('gpt-4.1')).toHaveAttribute('dir', 'ltr')
    expect(screen.getByText('البث')).toBeInTheDocument()
    expect(screen.getByText('الرؤية')).toBeInTheDocument()
    expect(screen.getByText('متاح')).toBeInTheDocument()
    expect(screen.getByText('⁨$2.5⁩/⁨$10⁩ لكل مليون رمز (إدخال/إخراج)')).toBeInTheDocument()
    expect(screen.getByText(/128,000.*إدخال/)).toBeInTheDocument()
    expect(screen.getByText('مزامنة من المزوّد')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('reviews a catalog sync in an RTL dialog, in Arabic', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: /^توسيع نماذج ⁨OpenAI⁩$/ }))
    await screen.findByText('GPT-4.1')
    fireEvent.click(screen.getByText('مزامنة من المزوّد'))

    expect(await screen.findByText('جديد لدى المزوّد (1)')).toBeInTheDocument()
    expect(
      screen.getByText('مزامنة فهرس OpenAI من المزوّد'.replace('OpenAI', '⁨OpenAI⁩')),
    ).toBeInTheDocument()
    expect(screen.getByText('لم يعد مدرجًا لدى المزوّد (1)')).toBeInTheDocument()
    expect(screen.getByText('لا عناصر محددة')).toBeInTheDocument()
    expect(screen.getByText('ستُضاف بحالة غير متاح')).toBeInTheDocument()
    expect(screen.getByText('gpt-5')).toHaveAttribute('dir', 'ltr')
    expect(screen.getByRole('dialog', { hidden: true }).closest('[dir]')).toHaveAttribute(
      'dir',
      'rtl',
    )

    fireEvent.click(screen.getByLabelText('تحديد ⁨GPT-5⁩'))
    expect(screen.getByText('عنصر واحد محدد')).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it('opens the credential dialog and the confirm dialog in Arabic', async () => {
    renderPage()
    fireEvent.click(
      await screen.findByRole('button', { name: /^استبدال بيانات الاعتماد لـ ⁨OpenAI⁩$/ }),
    )
    expect(await screen.findByText('لن تُعرض القيمة مرة أخرى بعد حفظها.')).toBeInTheDocument()
    expect(screen.getByLabelText(/^مفتاح API/)).toBeInTheDocument()
    fireEvent.click(screen.getByText('تأكيد'))
    expect(await screen.findByText('مفتاح API مطلوب.')).toBeInTheDocument()
  })

  it('shows the custom models section in Arabic, with failure kinds and a plural file count', async () => {
    const fallbacks = watchI18nFallbacks()
    renderPage()

    expect(await screen.findByText('النماذج المخصصة')).toBeInTheDocument()
    expect(await screen.findByText('broken-model')).toBeInTheDocument()
    expect(screen.getByText('FTP عادي')).toBeInTheDocument()
    expect(screen.getByText('فشل')).toBeInTheDocument()
    expect(screen.getByText('انقطع الاتصال')).toBeInTheDocument()
    expect(screen.getByText('انقطع الاتصال بوجهة النشر.')).toBeInTheDocument()
    expect(screen.getByText('1.5 GB')).toHaveAttribute('dir', 'ltr')
    // Two overwritten files is the Arabic dual form.
    expect(screen.getByText('ملفان مستبدَلان')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إضافة نموذج' })).toBeInTheDocument()
    expect(fallbacks.calls()).toEqual([])
    fallbacks.restore()
  })

  it.each(THEME_MODES)('has no axe violations in %s mode', async (mode) => {
    setTheme(mode)
    const { container } = renderPage()
    await screen.findByText('broken-model')
    await waitFor(() => expect(screen.getByText('قد يكون قديمًا')).toBeInTheDocument())

    expect(await axe(container)).toHaveNoViolations()
  })
})
