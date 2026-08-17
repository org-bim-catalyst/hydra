import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import * as voiceApi from '../api/voiceApi'
import type { PagedResult, PersistedMessage } from '../api/chatsApi'
import { useVoicePreferencesStore } from '../voice/voicePreferencesStore'
import type { useVoiceOutput } from '../voice/useVoiceOutput'
import { useWorkspaceOverlayStore } from '../../../store/workspaceOverlayStore'
import { useComingSoonStore } from '../../../store/comingSoonStore'
import { ChatPage, ConversationView } from './ChatPage'

vi.mock('../api/voiceApi', async () => {
  const actual = await vi.importActual<typeof voiceApi>('../api/voiceApi')
  return { ...actual, getVoicePreferences: vi.fn(), saveVoicePreferences: vi.fn() }
})

const CHAT_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const CHAT_B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'

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

function makeMessage(overrides: Partial<PersistedMessage>): PersistedMessage {
  return {
    id: 'msg-1',
    role: 'Assistant',
    kind: 'Text',
    content: 'Hello there',
    sourceText: null,
    createdAtUtc: '2026-07-29T10:00:00Z',
    provider: 'OpenAI',
    model: 'gpt-4',
    generationParametersJson: null,
    inputTokenCount: null,
    outputTokenCount: null,
    attachments: [],
    citations: [],
    ...overrides,
  }
}

function messagesPage(items: PersistedMessage[]): PagedResult<PersistedMessage> {
  return { items, nextCursor: null }
}

/** Builds an SSE-formatted ReadableStream matching aiApi.ts's parser (`data: {chunk}\n\n`, ending `data: [DONE]\n\n`). */
function sseStream(chunks: string[], firstChunkDelayMs = 0): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder()
  return new ReadableStream({
    async start(controller) {
      if (firstChunkDelayMs > 0) {
        await new Promise((resolve) => setTimeout(resolve, firstChunkDelayMs))
      }
      for (const chunk of chunks) {
        controller.enqueue(encoder.encode(`data: ${chunk}\n\n`))
      }
      controller.enqueue(encoder.encode('data: [DONE]\n\n'))
      controller.close()
    },
  })
}

// specs/005-multi-provider-ai-engine: ChatComposer now stays disabled until a provider/model
// is selected, so every test that sends a message needs the catalog to resolve to something —
// these are base handlers (survive `server.resetHandlers()`), not per-test overrides.
const server = setupServer(
  http.get('*/api/v1/ai/providers', () =>
    HttpResponse.json([
      {
        id: 'provider-1',
        providerKey: 'openai',
        displayName: 'OpenAI',
        healthStatus: 'Healthy',
        healthStatusCheckedAtUtc: null,
      },
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
)

beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

// jsdom reports zero layout size, so @tanstack/react-virtual would otherwise compute zero
// visible rows — give the scroll container a plausible height so the virtualized list
// actually renders its items (same workaround as ChatSidebar.a11y.test.tsx).
beforeEach(() => {
  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockReturnValue(600)
  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockReturnValue(56)
  // jsdom does not implement scrollIntoView at all (spyOn requires an existing property);
  // ConversationView calls it on every messages change.
  HTMLElement.prototype.scrollIntoView = vi.fn()
  // Resets voicePreferencesStore for every test in this file, not just the describe blocks
  // that explicitly exercise it — without this, a test that lands on Continuous mode (e.g.
  // the hydration test) would leak that into unrelated tests, triggering an unwanted
  // `recognition.start()`/`createSttSession` call via ConversationView's auto-start effect.
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

function renderConversation(chatId: string | null) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <ConversationView
          chatId={chatId}
          language="en"
          onLanguageChange={() => {}}
          onChatCreated={() => {}}
          tts={mockTts}
        />
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('ChatPage — voice preference hydration (SPEC-013 Foundational, FR-011/SC-004)', () => {
  function renderChatPage() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <ChatPage />
        </QueryClientProvider>
      </MemoryRouter>,
    )
  }

  beforeEach(() => {
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

  it('calls hydrateFromServer on mount and reflects the server-provided preferences', async () => {
    vi.mocked(voiceApi.getVoicePreferences).mockResolvedValue({
      conversationMode: 'Continuous',
      isMuted: true,
      selectedVoiceId: null,
      voiceSpeed: null,
      voiceStyle: null,
      preferredMicrophoneDeviceId: null,
      preferredSpeakerDeviceId: null,
    })

    renderChatPage()

    await waitFor(() => expect(voiceApi.getVoicePreferences).toHaveBeenCalled())
    await waitFor(() => {
      expect(useVoicePreferencesStore.getState().isMuted).toBe(true)
      expect(useVoicePreferencesStore.getState().conversationMode).toBe('Continuous')
    })
  })
})

describe('ChatPage — Studio workspace shell (SPEC-024 US1, FR-001/FR-004/FR-024)', () => {
  beforeEach(() => {
    useWorkspaceOverlayStore.setState({ expandedControlId: null, viewMode: '3D', unreadControlIds: new Set() })
    useComingSoonStore.setState({ featureLabel: null })
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

  it('sets the page title to "Flumeria Studio" (FR-001)', () => {
    renderChatPage()
    expect(document.title).toBe('Flumeria Studio')
  })

  it('exposes no permanent toolbar/navigation landmark, only reachable circular controls (FR-004)', () => {
    renderChatPage()
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument()
    expect(screen.queryByRole('banner')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Account' })).toBeInTheDocument()
  })

  it('reaches every account-menu destination and the theme toggle through the account control (FR-024)', () => {
    renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'Account' }))

    for (const label of [
      'Profile',
      'Settings',
      'Documents',
      'Knowledge Bases',
      'Memory Center',
      'Prompts',
      'Agents',
      'Workflows',
      'Privacy Policy',
      'Toggle theme',
      'Log out',
    ]) {
      expect(screen.getByRole('button', { name: label })).toBeInTheDocument()
    }
  })

  it('shows real icon actions (not placeholder text) for layers/navigation/selection/analysis, opening a "coming soon" dialog on click (FR-012/FR-021)', () => {
    // fireEvent (not userEvent) throughout this describe block for click interactions:
    // userEvent's pointer-events computed-style check hits a jsdom CSS-engine bug against
    // this tree's animated gradient/transition styles; fireEvent dispatches directly.
    // Dialog assertions use raw DOM queries (not getByRole) for the same reason — MUI
    // Dialog's Modal/Backdrop/Grow tree triggers the same jsdom computed-style bug when
    // getByRole's accessibility-tree walk runs against it.
    const { container } = renderChatPage()
    // Once the dialog has mounted once, it appears to poison every subsequent
    // testing-library role query in this render (not just ones targeting the dialog
    // itself) against the same jsdom bug — so every lookup below, before and after,
    // uses a plain CSS attribute selector instead of getByRole.
    const byLabel = (label: string) => container.querySelector<HTMLElement>(`button[aria-label="${label}"]`)!

    for (const [buttonLabel, actionLabel] of [
      ['Layers', 'Base map'],
      ['Navigation', 'Explore'],
      ['Selection', 'Marquee select'],
      ['Analysis', 'Sunlight'],
    ] as const) {
      fireEvent.click(byLabel(buttonLabel))
      const actionButton = byLabel(actionLabel)
      expect(actionButton).toBeInTheDocument()
      fireEvent.click(actionButton)
      const dialog = container.ownerDocument.querySelector('[role="dialog"]')
      expect(dialog).toHaveTextContent(`${buttonLabel} is coming soon to the Studio workspace.`)
      // the (always-mounted, currently-collapsed) chat FloatingPanel has its own "Close"
      // button elsewhere in the tree — scope this query to the dialog itself.
      fireEvent.click(dialog!.querySelector('button[aria-label="Close"]')!)
      // collapse the tool control again before the next iteration so only one is ever expanded
      fireEvent.click(byLabel(buttonLabel))
    }
  })

  it('switching the view mode visibly changes the workspace surface state (FR-011)', () => {
    const { container } = renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'View mode' }))
    fireEvent.click(screen.getByRole('button', { name: '2D' }))

    expect(container.querySelector('[data-view-mode="2D"]')).toBeInTheDocument()
  })

  it('expanding one tool control collapses whatever was previously expanded (FR-015)', () => {
    renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'Layers' }))
    expect(screen.getByRole('button', { name: 'Layers' })).toHaveAttribute('aria-expanded', 'true')

    fireEvent.click(screen.getByRole('button', { name: 'Navigation' }))
    expect(screen.getByRole('button', { name: 'Layers' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByRole('button', { name: 'Navigation' })).toHaveAttribute('aria-expanded', 'true')

    fireEvent.click(screen.getByRole('button', { name: 'Account' }))
    expect(screen.getByRole('button', { name: 'Navigation' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByRole('button', { name: 'Account' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('Tab visits every control, in the same top-cluster → right-stack → bottom-end order they render (FR-009, US4)', async () => {
    const user = userEvent.setup()
    renderChatPage()

    for (const label of [
      'Toggle theme',
      'Account',
      'View mode',
      'Layers',
      'Navigation',
      'Selection',
      'Analysis',
      'Chat with Lucy',
    ]) {
      await user.tab()
      expect(document.activeElement).toHaveAccessibleName(label)
    }
  })

  it('Enter expands a focused control, Tab reaches its revealed content, and Escape collapses it and returns focus (FR-007/FR-009, US4)', async () => {
    const user = userEvent.setup()
    renderChatPage()

    const viewModeButton = screen.getByRole('button', { name: 'View mode' })
    viewModeButton.focus()
    await user.keyboard('{Enter}')
    expect(viewModeButton).toHaveAttribute('aria-expanded', 'true')

    await user.tab()
    expect(document.activeElement).toHaveAccessibleName('2D')

    await user.keyboard('{Escape}')
    expect(viewModeButton).toHaveAttribute('aria-expanded', 'false')
    expect(viewModeButton).toHaveFocus()
  })

  it('Space also expands a focused control (FR-009, US4)', async () => {
    const user = userEvent.setup()
    renderChatPage()

    const layersButton = screen.getByRole('button', { name: 'Layers' })
    layersButton.focus()
    await user.keyboard(' ')
    expect(layersButton).toHaveAttribute('aria-expanded', 'true')
  })

  it('every control remains reachable under a simulated narrow (mobile) viewport (FR-020, US5)', () => {
    // jsdom has no real box layout (getBoundingClientRect returns zeros), so pixel-perfect
    // non-overlap isn't meaningfully assertable here — that's quickstart.md Scenario 5's
    // job in a real browser. This verifies the structural claim automated tests *can* make:
    // every control stays present/reachable once FloatingToolbar's mobile breakpoint
    // (matched via matchMedia) is active, not silently dropped or hidden.
    const originalMatchMedia = window.matchMedia
    window.matchMedia = ((query: string) => ({
      matches: query.includes('max-width'),
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    })) as typeof window.matchMedia

    try {
      renderChatPage()
      for (const label of ['View mode', 'Layers', 'Navigation', 'Selection', 'Analysis', 'Chat with Lucy', 'Account']) {
        expect(screen.getByRole('button', { name: label })).toBeInTheDocument()
      }
    } finally {
      window.matchMedia = originalMatchMedia
    }
  })

  it('sends a message through the chat control and streams a response with zero behavior change (FR-014/SC-006)', async () => {
    // Mounts the full ChatPage (all seven controls, AssistantPanel, ConversationSwitcher,
    // virtualizer) rather than the lighter renderConversation() other send/stream tests in
    // this file use — needs more than the 5s default given the provider-catalog fetch +
    // typing + SSE stream all happen sequentially on top of that heavier mount.
    server.use(
      http.get(`*/api/v1/chats`, () => HttpResponse.json({ items: [], nextCursor: null })),
      // No chat is selected yet (fresh workspace) — send() auto-creates one first.
      http.post('*/api/v1/chats', () =>
        HttpResponse.json({ id: CHAT_A, title: 'Hi Lucy', createdAtUtc: '2026-08-16T00:00:00Z', modifiedAtUtc: null }),
      ),
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => HttpResponse.json({ items: [], nextCursor: null })),
      http.post('*/api/v1/ai/chat', () => {
        const stream = sseStream(['Hello from the chat control'])
        return new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream' } })
      }),
    )
    const user = userEvent.setup()
    renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'Chat with Lucy' }))
    await screen.findByText('Start a conversation with Ask Lucy.')

    await waitFor(() => expect(screen.getByPlaceholderText('Message Ask Lucy...')).toBeEnabled())
    await user.type(screen.getByPlaceholderText('Message Ask Lucy...'), 'Hi Lucy')
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    expect(await screen.findByText('Hello from the chat control')).toBeInTheDocument()
  }, 15000)

  it('collapsing chat leaves the rest of the workspace interactive', () => {
    renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'Chat with Lucy' }))
    expect(screen.getByRole('button', { name: 'Chat with Lucy' })).toHaveAttribute('aria-expanded', 'true')

    fireEvent.click(screen.getByRole('button', { name: 'Chat with Lucy' }))
    expect(screen.getByRole('button', { name: 'Chat with Lucy' })).toHaveAttribute('aria-expanded', 'false')

    // The rest of the workspace (a different control) still responds normally afterward.
    fireEvent.click(screen.getByRole('button', { name: 'Layers' }))
    expect(screen.getByRole('button', { name: 'Layers' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('successfully signs the user out via the account control (FR-024)', async () => {
    let logoutRequested = false
    server.use(
      http.post('*/auth/logout', () => {
        logoutRequested = true
        return new HttpResponse(null, { status: 204 })
      }),
    )
    renderChatPage()

    // fireEvent (not userEvent) here: jsdom's CSS engine has a known issue resolving
    // computed styles for some elements in this larger tree when userEvent's
    // pointer-events/accessibility-tree checks run; fireEvent dispatches directly.
    fireEvent.click(screen.getByRole('button', { name: 'Account' }))
    fireEvent.click(screen.getByText('Log out'))

    await waitFor(() => expect(logoutRequested).toBe(true))
  })
})

describe('ConversationView — mute control (SPEC-013 US1, FR-001/FR-003/FR-012)', () => {
  beforeEach(() => {
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

  it('surfaces a visible error instead of failing silently when saving the mute preference fails', async () => {
    vi.mocked(voiceApi.saveVoicePreferences).mockRejectedValue(new Error('Could not save.'))
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    await user.click(await screen.findByRole('button', { name: /^mute$/i }))

    expect(await screen.findByText('Could not save.')).toBeInTheDocument()
  })

  it('mutes without error when saving succeeds', async () => {
    vi.mocked(voiceApi.saveVoicePreferences).mockImplementation((preference) =>
      Promise.resolve(preference),
    )
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    await user.click(await screen.findByRole('button', { name: /^mute$/i }))

    await waitFor(() => expect(useVoicePreferencesStore.getState().isMuted).toBe(true))
    expect(screen.queryByText('Could not save.')).not.toBeInTheDocument()
  })
})

describe('ConversationView — conversation loading (User Story 1 & 2)', () => {
  it('shows the empty-state placeholder when no conversation is selected', () => {
    renderConversation(null)
    expect(screen.getByText('Start a conversation with Ask Lucy.')).toBeInTheDocument()
  })

  it('never shows the empty-state placeholder while the selected conversation is pending', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, async () => {
        await delay('infinite')
        return HttpResponse.json(messagesPage([]))
      }),
    )
    renderConversation(CHAT_A)

    expect(await screen.findByRole('status', { name: 'Loading conversation…' })).toBeInTheDocument()
    expect(screen.queryByText('Start a conversation with Ask Lucy.')).not.toBeInTheDocument()
  })

  it('shows a loading spinner when a conversation is selected and its messages are pending', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, async () => {
        await delay('infinite')
        return HttpResponse.json(messagesPage([]))
      }),
    )
    renderConversation(CHAT_A)

    expect(await screen.findByRole('status', { name: 'Loading conversation…' })).toBeInTheDocument()
  })

  it('replaces the loading spinner with the conversation messages once the fetch resolves', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () =>
        HttpResponse.json(messagesPage([makeMessage({ id: 'm1', content: 'Hi from history' })])),
      ),
    )
    renderConversation(CHAT_A)

    expect(await screen.findByText('Hi from history')).toBeInTheDocument()
    expect(screen.queryByRole('status', { name: 'Loading conversation…' })).not.toBeInTheDocument()
  })

  it('shows a visible error state with a Retry button when the selected conversation fails to load', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => new HttpResponse(null, { status: 500 })),
    )
    renderConversation(CHAT_A)

    expect(await screen.findByRole('alert')).toHaveTextContent('Failed to load this conversation')
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(screen.queryByText('Start a conversation with Ask Lucy.')).not.toBeInTheDocument()
  })

  it('clicking Retry re-fetches and shows the conversation messages after a prior load failure', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => new HttpResponse(null, { status: 500 }), {
        once: true,
      }),
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () =>
        HttpResponse.json(messagesPage([makeMessage({ id: 'm1', content: 'Recovered message' })])),
      ),
    )
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    await screen.findByRole('button', { name: 'Retry' })
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    expect(await screen.findByText('Recovered message')).toBeInTheDocument()
  })

  it('shows the last-selected conversation when clicking B before A resolves (FR-005)', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, async () => {
        await new Promise((resolve) => setTimeout(resolve, 100))
        return HttpResponse.json(
          messagesPage([makeMessage({ id: 'a1', content: 'Conversation A content' })]),
        )
      }),
      http.get(`*/api/v1/chats/${CHAT_B}/messages`, () =>
        HttpResponse.json(
          messagesPage([makeMessage({ id: 'b1', content: 'Conversation B content' })]),
        ),
      ),
    )

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { rerender } = render(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <ConversationView
            key={CHAT_A}
            chatId={CHAT_A}
            language="en"
            onLanguageChange={() => {}}
            onChatCreated={() => {}}
            tts={mockTts}
          />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    // Simulate the ChatPage remount-via-key behavior of selecting a second conversation
    // before the first one's slower fetch has resolved.
    rerender(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <ConversationView
            key={CHAT_B}
            chatId={CHAT_B}
            language="en"
            onLanguageChange={() => {}}
            onChatCreated={() => {}}
            tts={mockTts}
          />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    expect(await screen.findByText('Conversation B content')).toBeInTheDocument()
    // Give A's slower response a chance to resolve, then confirm it never overwrote B's view.
    await new Promise((resolve) => setTimeout(resolve, 150))
    expect(screen.queryByText('Conversation A content')).not.toBeInTheDocument()
    expect(screen.getByText('Conversation B content')).toBeInTheDocument()
  })
})

describe('ConversationView — thinking indicator & send retry (User Story 3)', () => {
  it('shows ThinkingIndicator instead of a message bubble while no reply content has streamed in yet', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => HttpResponse.json(messagesPage([]))),
      http.post('*/api/v1/ai/chat', () => {
        const stream = sseStream(['Hello'], 100)
        return new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream' } })
      }),
    )
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    // The composer stays disabled until the provider/model catalog resolves and auto-selects
    // (specs/005-multi-provider-ai-engine) — wait for that before typing, since userEvent
    // cannot type into a disabled field.
    await waitFor(() => expect(screen.getByPlaceholderText('Message Ask Lucy...')).toBeEnabled())
    await user.type(screen.getByPlaceholderText('Message Ask Lucy...'), 'Hi Lucy')
    await user.click(screen.getByRole('button', { name: 'Send message' }))

    expect(await screen.findByRole('status', { name: 'Ask Lucy is thinking' })).toBeInTheDocument()
  })

  it('replaces ThinkingIndicator with streamed content once the first chunk arrives', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => HttpResponse.json(messagesPage([]))),
      http.post('*/api/v1/ai/chat', () => {
        const stream = sseStream(['Hello there'], 20)
        return new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream' } })
      }),
    )
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    // The composer stays disabled until the provider/model catalog resolves and auto-selects
    // (specs/005-multi-provider-ai-engine) — wait for that before typing, since userEvent
    // cannot type into a disabled field.
    await waitFor(() => expect(screen.getByPlaceholderText('Message Ask Lucy...')).toBeEnabled())
    await user.type(screen.getByPlaceholderText('Message Ask Lucy...'), 'Hi Lucy')
    await user.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(screen.getByText('Hello there')).toBeInTheDocument())
    expect(screen.queryByRole('status', { name: 'Ask Lucy is thinking' })).not.toBeInTheDocument()
  })

  it('surfaces a Retry-able Snackbar error on a failed send and resends the same content', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => HttpResponse.json(messagesPage([]))),
      http.post('*/api/v1/ai/chat', () => new HttpResponse(null, { status: 500 }), { once: true }),
      http.post('*/api/v1/ai/chat', () => {
        const stream = sseStream(['Recovered reply'])
        return new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream' } })
      }),
    )
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    // The composer stays disabled until the provider/model catalog resolves and auto-selects
    // (specs/005-multi-provider-ai-engine) — wait for that before typing, since userEvent
    // cannot type into a disabled field.
    await waitFor(() => expect(screen.getByPlaceholderText('Message Ask Lucy...')).toBeEnabled())
    await user.type(screen.getByPlaceholderText('Message Ask Lucy...'), 'Hi Lucy')
    await user.click(screen.getByRole('button', { name: 'Send message' }))

    await screen.findByRole('button', { name: 'Retry' })
    await user.click(screen.getByRole('button', { name: 'Retry' }))

    await waitFor(() => expect(screen.getByText('Recovered reply')).toBeInTheDocument())
  })
})

describe('ConversationView — reopening a newly-created conversation (User Story 5)', () => {
  it('shows real messages when reopened after its first read captured an empty/stale snapshot', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () => HttpResponse.json(messagesPage([])), {
        once: true,
      }),
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () =>
        HttpResponse.json(
          messagesPage([
            makeMessage({ id: 'u1', role: 'User', content: 'Hi Lucy' }),
            makeMessage({ id: 'a1', content: 'Hello! How can I help?' }),
          ]),
        ),
      ),
    )

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const renderAt = (viewKey: number) =>
      render(
        <MemoryRouter>
          <QueryClientProvider client={queryClient}>
            <ConversationView
              key={viewKey}
              chatId={CHAT_A}
              language="en"
              onLanguageChange={() => {}}
              onChatCreated={() => {}}
              tts={mockTts}
            />
          </QueryClientProvider>
        </MemoryRouter>,
      )

    // First mount: captures the empty snapshot — matching a fetch that raced an in-progress
    // reply for a chat that was just auto-created mid-send.
    const first = renderAt(1)
    await waitFor(() =>
      expect(queryClient.getQueryData(['chats', CHAT_A, 'messages'])).toBeDefined(),
    )
    first.unmount()

    // Second mount (user navigated away and back): the cache still holds the stale empty
    // result, but the background refetch should deliver — and this view should display —
    // the real, now-persisted messages, not stay blank forever.
    renderAt(2)

    expect(await screen.findByText('Hello! How can I help?')).toBeInTheDocument()
  })

  it('continues syncing later-arriving paginated pages into the displayed messages', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, ({ request }) => {
        const cursor = new URL(request.url).searchParams.get('cursor')
        if (!cursor) {
          return HttpResponse.json({
            items: [makeMessage({ id: 'p1', content: 'Page one message' })],
            nextCursor: 'page2',
          })
        }
        return HttpResponse.json({
          items: [makeMessage({ id: 'p2', content: 'Page two message' })],
          nextCursor: null,
        })
      }),
    )

    renderConversation(CHAT_A)

    expect(await screen.findByText('Page one message')).toBeInTheDocument()
    expect(await screen.findByText('Page two message')).toBeInTheDocument()
  })
})
