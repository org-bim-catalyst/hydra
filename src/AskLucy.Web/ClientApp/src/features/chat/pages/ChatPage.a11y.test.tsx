import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { useWorkspaceOverlayStore } from '../../../store/workspaceOverlayStore'
import type { useVoiceOutput } from '../voice/useVoiceOutput'
import { useVoicePreferencesStore } from '../voice/voicePreferencesStore'
import { ChatPage, ConversationView } from './ChatPage'

expect.extend(toHaveNoViolations)

const CHAT_ID = 'cccccccc-cccc-cccc-cccc-cccccccccccc'

const mockTts: ReturnType<typeof useVoiceOutput> = {
  isSupported: true,
  speak: async () => {},
  stop: () => {},
  isSpeaking: false,
  getIntensity: () => 0,
  error: null,
  clearError: () => {},
  isMuted: false,
  setMuted: () => {},
  toggleMute: () => {},
}

const server = setupServer()

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(56)
  HTMLElement.prototype.scrollIntoView = vi.fn()
  useVoicePreferencesStore.setState({
    conversationMode: 'PushToTalk',
    isMuted: false,
    selectedVoiceId: null,
    voiceSpeed: null,
    voiceStyle: null,
    preferredMicrophoneDeviceId: null,
    preferredSpeakerDeviceId: null,
    error: null,
  })
})

function renderConversation() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <ConversationView
          chatId={CHAT_ID}
          language="en"
          onLanguageChange={() => {}}
          onChatCreated={() => {}}
          tts={mockTts}
        />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('ConversationView accessibility (constitution §7, §10)', () => {
  it('has no automatically detectable a11y violations while the loading spinner is shown', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_ID}/messages`, async () => {
        await new Promise(() => {}) // never resolves — keeps the query pending for the whole test
      }),
    )
    const { container, findByRole } = renderConversation()

    await findByRole('status', { name: 'Loading conversation…' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations in the error/Retry state', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_ID}/messages`, () => new HttpResponse(null, { status: 500 })),
    )
    const { container, findByRole } = renderConversation()

    await findByRole('alert')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

describe('ConversationView voice controls accessibility (SPEC-013 T020, constitution §7/§10)', () => {
  // Note: VoiceControlBar.test.tsx and ChatComposer.test.tsx already a11y-test the muted,
  // listening, and permission-denied states directly against those components with
  // deterministic props — this integration-level check instead covers a state that's only
  // reachable by the real, page-level `voicePreferencesStore` (unlike `isMuted`, which the
  // mocked `tts` here doesn't reactively reflect): Continuous mode's real structural
  // difference (no mic button rendered in `ChatComposer`).
  it('has no automatically detectable a11y violations in Continuous mode (no mic button, listening status only)', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_ID}/messages`, () =>
        HttpResponse.json({ items: [], nextCursor: null }),
      ),
    )
    useVoicePreferencesStore.setState({ conversationMode: 'Continuous' })
    const { container, findByLabelText } = renderConversation()

    await findByLabelText('Switch to Push-to-Talk mode')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

describe('ChatPage accessibility — chat panel (SPEC-006/SPEC-024, constitution §7/§10)', () => {
  beforeEach(() => {
    useWorkspaceOverlayStore.setState({ expandedControlId: null, viewMode: '3D', unreadControlIds: new Set() })
  })

  function renderChatPage() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <MemoryRouter initialEntries={['/studio']}>
        <QueryClientProvider client={queryClient}>
          <ChatPage />
        </QueryClientProvider>
      </MemoryRouter>,
    )
  }

  it('has no automatically detectable a11y violations with the chat panel open (FR-013)', async () => {
    const { container, findByRole, findByText } = renderChatPage()

    fireEvent.click(await findByRole('button', { name: 'Chat with Lucy' }))
    await findByText('Start a conversation with Ask Lucy.')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with the chat panel collapsed (default state) (FR-013/FR-014)', async () => {
    const { container, findByRole } = renderChatPage()

    await findByRole('button', { name: 'Chat with Lucy' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

describe('ChatPage accessibility — Studio workspace shell (SPEC-024, FR-019, constitution §7/§10)', () => {
  beforeEach(() => {
    useWorkspaceOverlayStore.setState({ expandedControlId: null, viewMode: '3D', unreadControlIds: new Set() })
  })

  function renderChatPage() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <MemoryRouter initialEntries={['/studio']}>
        <QueryClientProvider client={queryClient}>
          <ChatPage />
        </QueryClientProvider>
      </MemoryRouter>,
    )
  }

  it('has no automatically detectable a11y violations in the all-collapsed state (FR-019, US4)', async () => {
    const { container, findByRole } = renderChatPage()

    await findByRole('button', { name: 'Account' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it.each(['View mode', 'Layers', 'Navigation', 'Selection', 'Analysis', 'Account'])(
    'has no automatically detectable a11y violations with %s expanded (FR-019, US4)',
    async (label) => {
      const { container, findByRole } = renderChatPage()

      const trigger = await findByRole('button', { name: label })
      fireEvent.click(trigger)
      expect(trigger).toHaveAttribute('aria-expanded', 'true')

      const results = await axe(container)
      expect(results).toHaveNoViolations()
    },
  )
})
