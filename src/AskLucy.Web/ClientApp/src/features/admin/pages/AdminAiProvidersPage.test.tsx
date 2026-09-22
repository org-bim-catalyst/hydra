import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
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
}

const server = setupServer(
  http.get('*/api/v1/admin/ai/providers', () => HttpResponse.json([baseProvider])),
)

beforeAll(() => server.listen())
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
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
