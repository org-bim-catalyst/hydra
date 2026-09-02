import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SettingsPage } from './SettingsPage'

expect.extend(toHaveNoViolations)

const server = setupServer(
  http.get('*/api/v1/profile', () => HttpResponse.json({ email: 'lucy@example.com', firstName: 'Lucy' })),
  http.get('*/api/v1/auth/external-logins', () => HttpResponse.json([])),
  http.get('*/api/v1/ai/providers', () =>
    HttpResponse.json([
      { id: 'provider-1', providerKey: 'openai', displayName: 'OpenAI', healthStatus: 'Healthy', healthStatusCheckedAtUtc: null },
    ]),
  ),
  http.get('*/api/v1/ai/providers/provider-1/models', () =>
    HttpResponse.json([
      {
        id: 'model-1',
        modelKey: 'gpt-4',
        displayName: 'GPT-4',
        contextWindowTokens: 128000,
        maxOutputTokens: 4096,
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
        providerId: 'provider-1',
        providerDisplayName: 'OpenAI',
      },
    ]),
  ),
  http.get('*/api/v1/ai/preferences', () =>
    HttpResponse.json({
      defaultProviderId: 'provider-1',
      defaultModelId: 'model-1',
      defaultGenerationParameters: null,
      isPlatformDefault: false,
    }),
  ),
  http.get('*/api/v1/ai/voice/preferences', () =>
    HttpResponse.json({
      conversationMode: 'PushToTalk',
      isMuted: false,
      selectedVoiceId: null,
      voiceSpeed: null,
      voiceStyle: null,
      preferredMicrophoneDeviceId: null,
      preferredSpeakerDeviceId: null,
    }),
  ),
  http.get('*/api/v1/chats*', () => HttpResponse.json({ items: [], nextCursor: null })),
  http.get('*/api/v1/users/me/cookie-consent', () =>
    HttpResponse.json({
      hasConsented: true,
      requiresReconsent: false,
      policyVersion: '1',
      currentPolicyVersion: '1',
      essential: true,
      functional: true,
      analytics: false,
      marketing: false,
      lastUpdatedAtUtc: '2026-08-01T00:00:00Z',
    }),
  ),
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

beforeEach(() => {
  // ChatHistoryTab's virtualized ConversationList needs a plausible scroll-container size
  // under jsdom (same workaround as ChatSidebar.a11y.test.tsx).
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(56)
  Object.defineProperty(navigator, 'mediaDevices', {
    configurable: true,
    value: { enumerateDevices: vi.fn().mockResolvedValue([]) },
  })
})

function renderSettings() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <SettingsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('SettingsPage accessibility (FR-004, SPEC-017 T043)', () => {
  it('has no automatically detectable a11y violations on the default (Security) tab', async () => {
    const { container, findByRole } = renderSettings()

    await findByRole('heading', { name: 'Settings' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

// specs/025-chat-configuration-settings, T029 — a full-page a11y sweep across all 8 tabs,
// post-integration (Chat Configuration and Chat History included), extending the
// single-tab check above.
// A distinctive, settled-state heading/label per tab to wait on before running axe, so the
// assertion reflects each tab's real (data-loaded) content rather than a transient loading
// state — SettingsPage's `TabPanel` doesn't render an ARIA `tabpanel` role to key off of.
const SETTLED_CONTENT_BY_TAB: Record<string, string> = {
  Security: 'Change password',
  Account: 'Email address',
  Data: 'Download your data',
  Cookies: 'Cookie preferences',
}

describe('SettingsPage accessibility — full tab sweep (specs/025-chat-configuration-settings)', () => {
  it.each(Object.keys(SETTLED_CONTENT_BY_TAB))(
    'has no automatically detectable a11y violations on the %s tab',
    async (tabLabel) => {
      const user = userEvent.setup()
      const { container } = renderSettings()

      await screen.findByRole('heading', { name: 'Settings' })
      await user.click(screen.getByRole('tab', { name: tabLabel }))
      // Chat History's settled marker is a form control's accessible label, not literal text
      // content — findByText wouldn't match a TextField's aria-label/placeholder.
      if (tabLabel === 'Chat History') {
        await screen.findByLabelText(SETTLED_CONTENT_BY_TAB[tabLabel])
      } else {
        await screen.findByText(SETTLED_CONTENT_BY_TAB[tabLabel])
      }

      const results = await axe(container)
      expect(results).toHaveNoViolations()
    },
  )
})
