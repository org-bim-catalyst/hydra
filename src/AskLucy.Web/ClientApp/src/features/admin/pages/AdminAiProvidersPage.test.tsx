import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'
import { AdminAiProvidersPage } from './AdminAiProvidersPage'

const baseProvider: AdminAiProvider = {
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

function sessionWith(permissions: string[]) {
  return http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: [], permissions }),
  )
}

const server = setupServer(
  sessionWith(['admin.ai-providers.view', 'admin.custom-models.view']),
  http.get('*/api/v1/admin/ai/providers', () => HttpResponse.json([baseProvider])),
  http.get('*/api/v1/admin/custom-models/deployment-status', () =>
    HttpResponse.json({ isConfigured: true, transport: 'FTPS', maxDeploymentBytes: 2 ** 30 }),
  ),
  http.get('*/api/v1/admin/custom-models', () =>
    HttpResponse.json({ items: [], page: 1, pageSize: 50, totalCount: 0 }),
  ),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage(initialEntry = '/admin/ai-providers') {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[initialEntry]}>
        <AdminAiProvidersPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminAiProvidersPage — credential hint (specs/066)', () => {
  it('renders the credential hint next to the provider name when a credential is configured (US1/US2)', async () => {
    const { findByText } = renderPage()

    await findByText('OpenAI')
    expect(await findByText('sk-p...33IA')).toBeInTheDocument()
  })

  it('renders "Not set" next to the provider name when no credential is configured (US1)', async () => {
    server.use(
      http.get('*/api/v1/admin/ai/providers', () =>
        HttpResponse.json([{ ...baseProvider, hasCredential: false, credentialHint: null }]),
      ),
    )
    const { findByText } = renderPage()

    await findByText('OpenAI')
    expect(await findByText('Not set')).toBeInTheDocument()
  })

  it('renders the hint text verbatim as returned by the API, without reformatting it (US2)', async () => {
    server.use(
      http.get('*/api/v1/admin/ai/providers', () =>
        HttpResponse.json([{ ...baseProvider, credentialHint: 'zzzz...WXYZ' }]),
      ),
    )
    const { findByText } = renderPage()

    expect(await findByText('zzzz...WXYZ')).toBeInTheDocument()
  })
})

describe('AdminAiProvidersPage — Custom models section (specs/072)', () => {
  it('shows the providers table and the Custom models section to an admin who can view both', async () => {
    const { findByText } = renderPage()

    expect(await findByText('OpenAI')).toBeInTheDocument()
    expect(await findByText('Custom models')).toBeInTheDocument()
    expect(await findByText('No custom models yet.')).toBeInTheDocument()
  })

  it('shows only the Custom models section, and never asks for providers, without admin.ai-providers.view', async () => {
    let providersRequested = false
    server.use(
      sessionWith(['admin.custom-models.view']),
      http.get('*/api/v1/admin/ai/providers', () => {
        providersRequested = true
        return HttpResponse.json([baseProvider])
      }),
    )
    const { findByText, queryByText } = renderPage()

    expect(await findByText('No custom models yet.')).toBeInTheDocument()
    expect(queryByText('OpenAI')).not.toBeInTheDocument()
    expect(providersRequested).toBe(false)
  })

  it('heads the providers table "Frontier models", beside the Custom models section', async () => {
    const { findByRole } = renderPage()

    expect(await findByRole('heading', { name: 'Frontier models' })).toBeInTheDocument()
    expect(await findByRole('heading', { name: 'Custom models' })).toBeInTheDocument()
  })

  it("lists a speech vendor like any other, without a kind badge beside its name", async () => {
    server.use(
      http.get('*/api/v1/admin/ai/providers', () =>
        HttpResponse.json([
          baseProvider,
          { ...baseProvider, id: 'provider-2', providerKey: 'elevenlabs', displayName: 'ElevenLabs', kind: 'Speech' },
        ]),
      ),
    )
    const { findByText, queryByText } = renderPage()

    await findByText('ElevenLabs')
    // Its models say what they do in the Capabilities column ("audio"), as every vendor's do.
    expect(queryByText('Speech')).not.toBeInTheDocument()
  })

  it('hides the Custom models section without admin.custom-models.view', async () => {
    server.use(sessionWith(['admin.ai-providers.view']))
    const { findByText, queryByText } = renderPage()

    await findByText('OpenAI')
    expect(queryByText('Custom models')).not.toBeInTheDocument()
  })
})

describe('AdminAiProvidersPage — ?select= deep link (specs/074 FR-017)', () => {
  const second = { ...baseProvider, id: 'provider-2', providerKey: 'anthropic', displayName: 'Anthropic' }

  it('selects and scrolls to the provider the link names', async () => {
    server.use(http.get('*/api/v1/admin/ai/providers', () => HttpResponse.json([baseProvider, second])))
    // jsdom has no layout, so scrollIntoView is missing from its elements.
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    const { findByText, getByText } = renderPage('/admin/ai-providers?select=provider-2')

    const row = (await findByText('Anthropic')).closest('tr')!
    expect(row).toHaveClass('Mui-selected')
    expect(getByText('OpenAI').closest('tr')).not.toHaveClass('Mui-selected')
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
    expect(scrollIntoView.mock.contexts[0]).toBe(row)
  })

  it('says so inline when the linked provider no longer exists', async () => {
    Element.prototype.scrollIntoView = vi.fn()
    const { findByText } = renderPage('/admin/ai-providers?select=provider-gone')

    expect(await findByText('The provider this link pointed to no longer exists.')).toBeInTheDocument()
  })
})
