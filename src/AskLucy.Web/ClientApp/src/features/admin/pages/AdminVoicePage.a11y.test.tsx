import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import type { AdminVoiceProvider, VoiceOption } from '../api/adminVoiceApi'
import { AdminVoicePage } from './AdminVoicePage'

expect.extend(toHaveNoViolations)

const providers: AdminVoiceProvider[] = [
  {
    id: 'provider-eleven',
    providerKey: 'ElevenLabs',
    displayName: 'ElevenLabs',
    priority: 0,
    isPrimary: true,
    defaultVoiceId: 'rachel',
    requiresCredential: true,
    hasCredential: false,
    credentialHint: null,
  },
  {
    id: 'provider-supertonic',
    providerKey: 'Supertonic',
    displayName: 'Supertonic (on-server)',
    priority: 1,
    isPrimary: false,
    defaultVoiceId: 'F1',
    requiresCredential: false,
    hasCredential: false,
    credentialHint: null,
  },
]

const voices: VoiceOption[] = [{ id: 'rachel', name: 'Rachel', gender: 'female', description: 'american, young' }]

const server = setupServer(
  http.get('*/api/v1/auth/session', () =>
    HttpResponse.json({ authenticated: true, userId: 'admin-1', roles: ['Administrator'], permissions: [] }),
  ),
  http.get('*/api/v1/admin/voice/providers', () => HttpResponse.json(providers)),
  http.get('*/api/v1/admin/voice/providers/:id/voices', () => HttpResponse.json(voices)),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

describe('AdminVoicePage accessibility', () => {
  it('has no automatically detectable a11y violations (constitution §10)', async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { container } = render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <AdminVoicePage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await waitFor(() =>
      expect(screen.getByLabelText('Voice', { selector: '[role="combobox"]' })).toHaveTextContent('Rachel'),
    )

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
