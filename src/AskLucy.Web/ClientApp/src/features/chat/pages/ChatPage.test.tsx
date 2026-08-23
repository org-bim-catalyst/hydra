import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import * as voiceApi from '../api/voiceApi'
import type { PagedResult, PersistedMessage } from '../api/chatsApi'
import { useActiveConversationStore } from '../activeConversationStore'
import { useChatPanelSizeStore } from '../chatPanelSizeStore'
import { useVoicePreferencesStore } from '../voice/voicePreferencesStore'
import type { useVoiceOutput } from '../voice/useVoiceOutput'
import { useWorkspaceOverlayStore } from '../../../store/workspaceOverlayStore'
import { useComingSoonStore } from '../../../store/comingSoonStore'
import { ChatPage, ConversationView } from './ChatPage'

vi.mock('../api/voiceApi', async () => {
  const actual = await vi.importActual<typeof voiceApi>('../api/voiceApi')
  return {
    ...actual,
    getVoicePreferences: vi.fn(),
    // specs/034-transcription-crash-gesture-and-continuous-view: voicePreferencesStore's
    // update() does `set(saved)` with whatever this resolves to — a bare `vi.fn()` (no
    // implementation) resolves to `undefined`, and zustand's `set(undefined)` replaces the
    // entire store state with `undefined` (a pre-existing store-vs-test-mock gap this file's
    // own mode-switch tests can trip over once anything awaits the save to actually settle).
    // Echoing the patch back is a reasonable "the save succeeded" simulation.
    saveVoicePreferences: vi.fn().mockImplementation(async (patch) => patch),
  }
})

// specs/034-transcription-crash-gesture-and-continuous-view: entering Continuous mode now
// triggers a real useConversationAudio().startTurn() call, which — unmocked — reaches down
// into useSpeechRecognition's getUserMedia/AudioWorkletNode/WebSocket chain this file's
// existing fakes (installed per-describe-block for Push-to-Talk's simpler recorder needs)
// don't cover. Mocked here the same way useConversationAudio.test.ts mocks its own
// dependencies one level down — no test in this file needs the hook's real internal behavior,
// only that ChatPage wires isVoiceViewActive/ContinuousVoiceView to it correctly.
const conversationAudioMock = {
  voiceState: 'Idle' as const,
  errorMessage: null as string | null,
  provider: 'primary' as const,
  degradedNoticeVisible: false,
  deviceNotice: null as string | null,
  clearDeviceNotice: vi.fn(),
  getReactiveIntensity: () => 0,
  setMuted: vi.fn(),
  startTurn: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn().mockResolvedValue(undefined),
  cancelListening: vi.fn(),
  clearError: vi.fn(),
}
vi.mock('../voice/useConversationAudio', () => ({
  useConversationAudio: () => conversationAudioMock,
}))

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
//
// specs/025-chat-configuration-settings, T021: the in-toolbar `ProviderModelSelector` that
// used to auto-select a provider/model on mount was removed (relocated to Chat Configuration
// in Settings) — `ConversationView` now seeds its selection from `/ai/preferences` (a brand
// new conversation) or `GET /chats/{id}` (reopening one), so both need a base handler here too.
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
  http.get('*/api/v1/ai/preferences', () =>
    HttpResponse.json({
      defaultProviderId: 'provider-1',
      defaultModelId: 'model-1',
      defaultGenerationParameters: null,
      isPlatformDefault: false,
    }),
  ),
  http.get('*/api/v1/chats/:id', ({ params }) =>
    HttpResponse.json({
      id: params.id,
      title: 'Test chat',
      providerId: 'provider-1',
      modelId: 'model-1',
    }),
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
  // specs/025-chat-configuration-settings: resets the shared active-conversation store for
  // every test in this file — otherwise a test that selects/creates a chat would leak that
  // id into a later test's `renderChatPage()`, which now seeds its initial `selectedChatId`
  // from this store (FR-007).
  useActiveConversationStore.setState({ activeChatId: null })
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
  // specs/030-composer-panel-refinements: resets the persisted panel-height preference for
  // every test in this file, so a test that toggles full-height doesn't leak that choice
  // into a later, unrelated test's `renderChatPage()`.
  localStorage.removeItem('ask-lucy-chat-panel-size')
  useChatPanelSizeStore.setState({ isFullHeight: false })
})

function renderConversation(chatId: string | null) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <ConversationView
          chatId={chatId}
          language="en"
          onChatCreated={() => {}}
          onNewChat={() => {}}
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

  it('fetches voice preferences on mount (useVoicePreferencesQuery) and reflects the server-provided values', async () => {
    vi.mocked(voiceApi.getVoicePreferences).mockResolvedValue({
      conversationMode: 'Continuous',
      isMuted: true,
      selectedVoiceId: null,
      voiceSpeed: null,
      voiceStyle: null,
      preferredMicrophoneDeviceId: null,
      preferredSpeakerDeviceId: null,
      defaultLanguage: null,
    })

    renderChatPage()

    await waitFor(() => expect(voiceApi.getVoicePreferences).toHaveBeenCalled())
    await waitFor(() => {
      expect(useVoicePreferencesStore.getState().isMuted).toBe(true)
      expect(useVoicePreferencesStore.getState().conversationMode).toBe('Continuous')
    })
  })

  // specs/029-fix-chat-widget-bugs T014, FR-001/FR-002, research.md Decision 3 — Bug 1's
  // regression test: a failed voice-preferences fetch must never show the old blocking,
  // full-width "An unexpected error occurred" Snackbar (which previously fired on every
  // chat load regardless of whether this ever happened), and chat/voice must stay fully
  // usable on defaults throughout.
  it('does not show a blocking error banner when the voice-preferences fetch fails, and stays usable on defaults', async () => {
    vi.mocked(voiceApi.getVoicePreferences).mockRejectedValue(new Error('Network error'))

    // renderConversation (not this block's renderChatPage) — it defaults to the Expanded
    // panel, so ChatComposer (which hosts the small indicator this failure should surface)
    // is actually in the DOM to assert against.
    renderConversation(CHAT_A)

    await waitFor(() => expect(voiceApi.getVoicePreferences).toHaveBeenCalled())
    // Give the query's error state time to settle before asserting its absence.
    await waitFor(() => expect(screen.queryByText(/an unexpected error occurred/i)).not.toBeInTheDocument())
    expect(screen.queryByText('Network error')).not.toBeInTheDocument()
    // Voice/chat functionality remains available — defaults are still in effect, nothing
    // is blocked by the failed fetch (FR-002).
    expect(useVoicePreferencesStore.getState().conversationMode).toBe('PushToTalk')
    expect(screen.getByRole('button', { name: /^mute lucy$/i })).toBeEnabled()
  })

  it('seeds the response language from the hydrated defaultLanguage preference, reflected by the header flag (FR-016/FR-017)', async () => {
    useWorkspaceOverlayStore.setState({
      expandedControlId: null,
      viewMode: 'isometric',
      unreadControlIds: new Set(),
    })
    vi.mocked(voiceApi.getVoicePreferences).mockResolvedValue({
      conversationMode: 'PushToTalk',
      isMuted: false,
      selectedVoiceId: null,
      voiceSpeed: null,
      voiceStyle: null,
      preferredMicrophoneDeviceId: null,
      preferredSpeakerDeviceId: null,
      defaultLanguage: 'fr',
    })

    renderChatPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Expand Ask Lucy assistant' }))

    expect(
      await screen.findByRole('img', { name: 'Response language: French' }),
    ).toBeInTheDocument()
  })
})

describe('ChatPage — Studio workspace shell (SPEC-024 US1, FR-001/FR-004/FR-024)', () => {
  beforeEach(() => {
    useWorkspaceOverlayStore.setState({
      expandedControlId: null,
      viewMode: 'isometric',
      unreadControlIds: new Set(),
    })
    useComingSoonStore.setState({ featureLabel: null })
  })

  function renderChatPage() {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const utils = render(
      <MemoryRouter initialEntries={['/studio']}>
        <QueryClientProvider client={queryClient}>
          <ChatPage />
        </QueryClientProvider>
      </MemoryRouter>,
    )
    return { ...utils, queryClient }
  }

  // specs/025-chat-configuration-settings: ConversationView (always mounted, even while the
  // chat panel is collapsed) resolves an `ai/preferences` query on mount to seed its model
  // selection (research.md-adjacent T021 change). Waiting for that settle via the query
  // cache (rather than a blind timeout) keeps this act()-safe before driving keyboard
  // interactions in the tests below, so userEvent isn't racing a concurrent re-render.
  async function waitForModelSeeding(queryClient: QueryClient) {
    await waitFor(() => expect(queryClient.getQueryData(['ai', 'preferences'])).toBeDefined())
  }

  it('sets the page title to "Flumeria Studio" (FR-001)', () => {
    renderChatPage()
    expect(document.title).toBe('Flumeria Studio')
  })

  it('renders the new viewer surface alongside an unaffected AiPresenceCard (specs/027-immersive-viewer-platform FR-004/SC-007)', async () => {
    renderChatPage()

    // The new full-viewport viewer replaces the old WorkspaceSurface gradient mount. jsdom
    // doesn't implement WebGL2 (`getContext('webgl2')` returns null), so this renders the
    // non-interactive fallback (FR-005) here — a real browser with WebGL support renders the
    // placeholder instead (features/viewer/components/ViewerSurface.test.tsx covers both).
    expect(screen.getByTestId(/^viewer-(placeholder|fallback)$/)).toBeInTheDocument()

    // AiPresenceCard (the pre-existing, bottom-left decorative-sphere presence card) is a
    // separate component this feature does not touch — it still renders and progresses
    // through its normal lazy-load lifecycle exactly as before (its own dedicated
    // AiPresenceCard.test.tsx covers the rest of its behavior in isolation).
    expect(await screen.findByAltText('Lucy')).toBeInTheDocument()
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
      'Chat Configuration',
      'Chat History',
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

  it('reaches Chat Configuration and Chat History from the workspace in two clicks or fewer (specs/025-chat-configuration-settings FR-011)', () => {
    renderChatPage()

    // Click 1: open the account control. Click 2: the destination itself — matches FR-011's
    // "two clicks or fewer" requirement for reaching either Settings destination from the
    // workspace.
    fireEvent.click(screen.getByRole('button', { name: 'Account' }))
    expect(screen.getByRole('button', { name: 'Chat Configuration' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Chat History' })).toBeInTheDocument()
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
    const byLabel = (label: string) =>
      container.querySelector<HTMLElement>(`button[aria-label="${label}"]`)!

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

  // The old "switching the view mode visibly changes the workspace surface state (FR-011)"
  // test asserted `WorkspaceSurface`'s `data-view-mode` gradient attribute — retired by
  // specs/027-immersive-viewer-platform, which replaces `WorkspaceSurface` with `ViewerSurface`
  // (T015) and repurposes this same control into the real isometric/plan camera toggle (T047),
  // wired to `viewerEngine.setViewMode` instead. Its replacement test lives in
  // workspaceControls.test.tsx once that wiring exists.

  it('expanding one tool control collapses whatever was previously expanded (FR-015)', () => {
    renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'Layers' }))
    expect(screen.getByRole('button', { name: 'Layers' })).toHaveAttribute('aria-expanded', 'true')

    fireEvent.click(screen.getByRole('button', { name: 'Navigation' }))
    expect(screen.getByRole('button', { name: 'Layers' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.getByRole('button', { name: 'Navigation' })).toHaveAttribute(
      'aria-expanded',
      'true',
    )

    fireEvent.click(screen.getByRole('button', { name: 'Account' }))
    expect(screen.getByRole('button', { name: 'Navigation' })).toHaveAttribute(
      'aria-expanded',
      'false',
    )
    expect(screen.getByRole('button', { name: 'Account' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('Tab visits every WorkspaceOverlay control, in the same top-cluster → right-stack order they render (FR-009, US4)', async () => {
    // specs/026-floating-chat-assistant: chat is no longer one of WorkspaceOverlay's
    // `controls` (research.md #1) — it's the bespoke ChatAssistantWidget, verified
    // separately below ("the collapsed chat widget's handle is keyboard-reachable...").
    const user = userEvent.setup()
    const { queryClient } = renderChatPage()
    await waitForModelSeeding(queryClient)

    for (const label of [
      'Toggle theme',
      'Stop rotation', // specs/027-immersive-viewer-platform: rotation defaults on (jsdom's stubbed matchMedia reports no reduced-motion preference)
      'Account',
      'View mode',
      'Layers',
      'Navigation',
      'Selection',
      'Analysis',
    ]) {
      await user.tab()
      expect(document.activeElement).toHaveAccessibleName(label)
    }
  })

  it('Enter expands a focused control, Tab reaches its revealed content, and Escape collapses it and returns focus (FR-007/FR-009, US4)', async () => {
    const user = userEvent.setup()
    const { queryClient } = renderChatPage()
    await waitForModelSeeding(queryClient)

    const viewModeButton = screen.getByRole('button', { name: 'View mode' })
    viewModeButton.focus()
    await user.keyboard('{Enter}')
    expect(viewModeButton).toHaveAttribute('aria-expanded', 'true')

    await user.tab()
    expect(document.activeElement).toHaveAccessibleName('Isometric')

    await user.keyboard('{Escape}')
    expect(viewModeButton).toHaveAttribute('aria-expanded', 'false')
    expect(viewModeButton).toHaveFocus()
  })

  it('Space also expands a focused control (FR-009, US4)', async () => {
    const user = userEvent.setup()
    const { queryClient } = renderChatPage()
    await waitForModelSeeding(queryClient)

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
      for (const label of [
        'View mode',
        'Layers',
        'Navigation',
        'Selection',
        'Analysis',
        'Account',
      ]) {
        expect(screen.getByRole('button', { name: label })).toBeInTheDocument()
      }
      // specs/026-floating-chat-assistant SC-001: the chat widget's Collapsed handle stays
      // reachable too, outside the WorkspaceOverlay `controls` cluster checked above.
      expect(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' })).toBeInTheDocument()
    } finally {
      window.matchMedia = originalMatchMedia
    }
  })

  it('sends a message through the chat control and streams a response with zero behavior change (FR-014/SC-006)', async () => {
    // Mounts the full ChatPage (all seven controls, ChatAssistantWidget, virtualizer) rather than
    // the lighter renderConversation() other send/stream tests in
    // this file use — needs more than the 5s default given the provider-catalog fetch +
    // typing + SSE stream all happen sequentially on top of that heavier mount.
    server.use(
      http.get(`*/api/v1/chats`, () => HttpResponse.json({ items: [], nextCursor: null })),
      // No chat is selected yet (fresh workspace) — send() auto-creates one first.
      http.post('*/api/v1/chats', () =>
        HttpResponse.json({
          id: CHAT_A,
          title: 'Hi Lucy',
          createdAtUtc: '2026-08-16T00:00:00Z',
          modifiedAtUtc: null,
        }),
      ),
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () =>
        HttpResponse.json({ items: [], nextCursor: null }),
      ),
      http.post('*/api/v1/ai/chat', () => {
        const stream = sseStream(['Hello from the chat control'])
        return new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream' } })
      }),
    )
    const user = userEvent.setup()
    renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' }))
    await screen.findByText('Start a conversation with Ask Lucy.')

    await waitFor(() => expect(screen.getByPlaceholderText('Message Ask Lucy...')).toBeEnabled())
    await user.type(screen.getByPlaceholderText('Message Ask Lucy...'), 'Hi Lucy')
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    expect(await screen.findByText('Hello from the chat control')).toBeInTheDocument()
  }, 15000)

  it('no longer renders a provider/model switcher or a conversation-history panel in the chat control (specs/025-chat-configuration-settings FR-008)', () => {
    renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' }))

    // Plain text/label queries (not getByRole) — role queries with a `name` filter hit a
    // known jsdom CSS-engine bug against this tree's animated/transition styles when hunting
    // for a role that has zero matches (same class of issue noted elsewhere in this file).
    expect(screen.queryByLabelText('Provider')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Model')).not.toBeInTheDocument()
    expect(screen.queryByText('Conversations')).not.toBeInTheDocument()
  })

  it('no longer renders a standalone "Generate image" button (specs/026-floating-chat-assistant FR-018)', () => {
    renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' }))

    expect(screen.queryByRole('button', { name: 'Generate image' })).not.toBeInTheDocument()
  })

  it('the minimal new-chat icon starts a fresh conversation without the old "+ New chat" text button (FR-012/FR-014)', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () =>
        HttpResponse.json(
          messagesPage([makeMessage({ id: 'a1', content: 'Existing conversation content' })]),
        ),
      ),
    )
    useActiveConversationStore.setState({ activeChatId: CHAT_A })
    renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' }))
    expect(await screen.findByText('Existing conversation content')).toBeInTheDocument()

    // FR-012: no prominent, text-labeled "+ New chat" control anywhere.
    expect(screen.queryByText('New chat')).not.toBeInTheDocument()
    expect(screen.queryByText('+ New chat')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Start new conversation' }))

    expect(await screen.findByText('Start a conversation with Ask Lucy.')).toBeInTheDocument()
    expect(useActiveConversationStore.getState().activeChatId).toBeNull()
  })

  it('collapsing chat leaves the rest of the workspace interactive', () => {
    renderChatPage()

    fireEvent.click(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' }))
    expect(screen.getByRole('button', { name: 'Collapse' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Collapse' }))
    expect(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' })).toBeInTheDocument()

    // The rest of the workspace (a different control) still responds normally afterward.
    fireEvent.click(screen.getByRole('button', { name: 'Layers' }))
    expect(screen.getByRole('button', { name: 'Layers' })).toHaveAttribute('aria-expanded', 'true')
  })

  it('toggling the resize control flips the persisted panel-height preference end-to-end (specs/030-composer-panel-refinements FR-008/FR-008a)', () => {
    renderChatPage()
    fireEvent.click(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' }))

    expect(useChatPanelSizeStore.getState().isFullHeight).toBe(false)
    fireEvent.click(screen.getByRole('button', { name: 'Expand to full height' }))

    expect(useChatPanelSizeStore.getState().isFullHeight).toBe(true)
    expect(screen.getByRole('button', { name: 'Collapse to half height' })).toBeInTheDocument()
    const stored = localStorage.getItem('ask-lucy-chat-panel-size')
    expect(stored).not.toBeNull()
    expect(JSON.parse(stored!).state.isFullHeight).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: 'Collapse to half height' }))
    expect(useChatPanelSizeStore.getState().isFullHeight).toBe(false)
  })

  it('the collapsed chat widget is keyboard-operable: reachable via Tab, Enter expands, Tab continues into content, Escape collapses and returns focus (FR-010/SC-007, T055)', async () => {
    // specs/026-floating-chat-assistant research.md #9/D1: ChatAssistantWidget does not
    // inherit CircularAction's ARIA-disclosure coverage, so it needs its own behavioral
    // (non-axe) keyboard test covering the full disclosure contract from T003/T009/T016.
    const user = userEvent.setup()
    renderChatPage()

    const handle = await screen.findByRole('button', { name: 'Expand Ask Lucy assistant' })
    expect(handle).toHaveAttribute('aria-expanded', 'false')
    expect(handle).not.toHaveAttribute('tabindex', '-1') // reachable via Tab, not skipped

    handle.focus()
    expect(handle).toHaveFocus()
    await user.keyboard('{Enter}')

    const collapseButton = await screen.findByRole('button', { name: 'Collapse' })
    expect(collapseButton).toBeInTheDocument()
    // CollapsedChatControl and ExpandedChatPanel are a ternary swap (not a dual-mount with
    // a single persistent trigger), so the expand handle itself unmounts on expand rather
    // than flipping its own aria-expanded to true; it is gone from the document instead.
    expect(
      screen.queryByRole('button', { name: 'Expand Ask Lucy assistant' }),
    ).not.toBeInTheDocument()
    // ExpandedChatPanel's own focus-management effect (mirroring FloatingPanel's) already
    // moved focus off the (now-unmounted) handle and into the revealed panel on open.
    expect(document.activeElement).not.toBe(document.body)
    const focusedAfterOpen = document.activeElement

    // Tab continues moving focus forward among the revealed panel's own content.
    await user.tab()
    expect(document.activeElement).not.toBe(document.body)
    expect(document.activeElement).not.toBe(focusedAfterOpen)

    await user.keyboard('{Escape}')
    const handleAgain = await screen.findByRole('button', { name: 'Expand Ask Lucy assistant' })
    expect(handleAgain).toHaveAttribute('aria-expanded', 'false')
    expect(handleAgain).toHaveFocus()
  })

  it('the collapsed chat widget also expands via Space, matching Enter (FR-010/SC-007, T055)', async () => {
    const user = userEvent.setup()
    renderChatPage()

    const handle = await screen.findByRole('button', { name: 'Expand Ask Lucy assistant' })
    handle.focus()
    await user.keyboard(' ')

    const collapseButton = await screen.findByRole('button', { name: 'Collapse' })
    expect(collapseButton).toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Expand Ask Lucy assistant' }),
    ).not.toBeInTheDocument()
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

  // specs/031-voice-controls-redesign FR-011, US6 — the mute control moved from the
  // composer footer into ExpandedChatPanel's header, next to Lucy's portrait, so it must
  // share an ancestor with the portrait image, not with the message text field.
  it('is reachable via the panel header (next to Lucy\'s portrait), not the composer footer', async () => {
    renderConversation(CHAT_A)

    const muteButton = await screen.findByRole('button', { name: /^mute lucy$/i })
    const portrait = screen.getByAltText('Ask Lucy')
    const textbox = screen.getByPlaceholderText('Message Ask Lucy...')

    const header = muteButton.closest('.MuiStack-root')
    expect(header).not.toBeNull()
    expect(within(header as HTMLElement).getByAltText('Ask Lucy')).toBe(portrait)
    expect(within(header as HTMLElement).queryByPlaceholderText('Message Ask Lucy...')).toBeNull()
    expect(textbox.closest('.MuiPaper-root')).not.toBe(header)
  })

  it('surfaces a visible error instead of failing silently when saving the mute preference fails', async () => {
    vi.mocked(voiceApi.saveVoicePreferences).mockRejectedValue(new Error('Could not save.'))
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    await user.click(await screen.findByRole('button', { name: /^mute lucy$/i }))

    expect(await screen.findByText('Could not save.')).toBeInTheDocument()
  })

  it('mutes without error when saving succeeds', async () => {
    vi.mocked(voiceApi.saveVoicePreferences).mockImplementation((preference) =>
      Promise.resolve(preference),
    )
    const user = userEvent.setup()
    renderConversation(CHAT_A)

    await user.click(await screen.findByRole('button', { name: /^mute lucy$/i }))

    await waitFor(() => expect(useVoicePreferencesStore.getState().isMuted).toBe(true))
    expect(screen.queryByText('Could not save.')).not.toBeInTheDocument()
  })
})

// specs/031-voice-controls-redesign FR-010, US5 — the translate control is removed
// entirely, superseding specs/029-fix-chat-widget-bugs's "relocated translate control".
describe('ConversationView — translate control removed (specs/031-voice-controls-redesign FR-010)', () => {
  it('renders no translate control anywhere on the page', async () => {
    renderConversation(CHAT_A)
    await screen.findByPlaceholderText('Message Ask Lucy...')
    expect(screen.queryByRole('button', { name: /translate/i })).not.toBeInTheDocument()
  })
})

describe('ConversationView — Push-to-Talk recording review (specs/026-floating-chat-assistant FR-019–FR-025)', () => {
  class FakeMediaRecorder {
    ondataavailable: ((event: { data: Blob }) => void) | null = null
    onstop: (() => void) | null = null
    mimeType = 'audio/webm'
    stream: MediaStream
    constructor(stream: MediaStream) {
      this.stream = stream
    }
    start = vi.fn(() => {
      this.ondataavailable?.({ data: new Blob(['audio'], { type: 'audio/webm' }) })
    })
    stop = vi.fn(() => {
      this.onstop?.()
    })
  }

  class FakeAnalyserNode {
    fftSize = 0
    frequencyBinCount = 32
    connect = vi.fn()
    disconnect = vi.fn()
    getByteFrequencyData = vi.fn((data: Uint8Array) => data.fill(64))
  }

  class FakeAudioContext {
    createMediaStreamSource = vi.fn(() => ({ connect: vi.fn() }))
    createAnalyser = vi.fn(() => new FakeAnalyserNode())
    close = vi.fn().mockResolvedValue(undefined)
  }

  const stopTrack = vi.fn()
  const fakeStream = { getTracks: () => [{ stop: stopTrack }] } as unknown as MediaStream
  let transcribeCalls = 0

  // Both VoiceControlBar and ChatComposer render their own "Start voice input" mic button
  // (pre-existing, both driven by the same recorder) — queries must pick one explicitly.
  const getMicButton = () => screen.getAllByRole('button', { name: 'Start voice input' })[0]
  const findMicButton = async () => {
    await waitFor(() =>
      expect(screen.getAllByRole('button', { name: 'Start voice input' }).length).toBeGreaterThan(
        0,
      ),
    )
    return getMicButton()
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
    transcribeCalls = 0
    server.use(
      http.post('*/api/v1/ai/transcriptions', () => {
        transcribeCalls += 1
        return HttpResponse.json({ text: 'transcribed text' })
      }),
    )
    vi.stubGlobal('AudioContext', FakeAudioContext)
    vi.stubGlobal('MediaRecorder', FakeMediaRecorder)
    // Adds mediaDevices without replacing the rest of jsdom's real `navigator` (which
    // userEvent/fireEvent's own internals depend on for realistic DOM interaction).
    Object.defineProperty(navigator, 'mediaDevices', {
      value: { getUserMedia: vi.fn(() => Promise.resolve(fakeStream)) },
      configurable: true,
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    Object.defineProperty(navigator, 'mediaDevices', { value: undefined, configurable: true })
  })

  // specs/034-transcription-crash-gesture-and-continuous-view FR-004/FR-006 — a genuine hold
  // (past the threshold) shows only the waveform throughout and releasing transcribes directly,
  // with no "Finished speaking" button or review step of any kind.
  it('press → hold (no transcript) → release transcribes directly and populates the field', async () => {
    renderConversation(CHAT_A)
    const micButton = await findMicButton()

    fireEvent.pointerDown(micButton)
    // The recorder's own start() is async (getUserMedia, AudioContext setup) — wait for it to
    // actually reach 'recording' before releasing, the same way a real hold has some non-zero
    // duration; without this, pointerUp can race ahead of phase becoming 'recording' and
    // finish() no-ops. `micButton` stays the same element throughout (specs/033) — its
    // aria-label flips once `isListening` becomes true.
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Stop voice input' })).toBeInTheDocument(),
    )
    // No live partial transcript anywhere in the composer while recording (FR-019), and no
    // "Finished speaking"/accept control of any kind while still held.
    expect(screen.getByPlaceholderText('Message Ask Lucy...')).toHaveValue('')
    expect(screen.queryByRole('button', { name: 'Finished speaking' })).not.toBeInTheDocument()
    expect(transcribeCalls).toBe(0)

    // specs/034: a tap and a hold resolve differently at release, based on elapsed hold
    // duration — a real (small but non-zero) delay here is what makes this a genuine hold
    // rather than a tap that would show review controls instead of auto-transcribing.
    await new Promise((resolve) => setTimeout(resolve, 400))
    fireEvent.pointerUp(micButton)

    expect(
      screen.queryByRole('button', { name: 'Send recording for transcription' }),
    ).not.toBeInTheDocument()
    await waitFor(() => expect(transcribeCalls).toBe(1))
    await waitFor(() =>
      expect(screen.getByPlaceholderText('Message Ask Lucy...')).toHaveValue('transcribed text'),
    )
  })

  // specs/034-transcription-crash-gesture-and-continuous-view's resolved clarification (carried
  // from specs/033): the dedicated mid-recording Cancel affordance is unreachable once a hold's
  // release always finishes directly (research.md Decision 3). Discarding an unwanted recording
  // now happens after the fact, by editing/not-sending the resulting draft text — there is no
  // pre-send cancel button for a genuine hold.
  it('no Cancel button appears during a held Push-to-Talk recording — discarding happens after transcription, not before', async () => {
    renderConversation(CHAT_A)
    const micButton = await findMicButton()

    fireEvent.pointerDown(micButton)
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Stop voice input' })).toBeInTheDocument(),
    )
    expect(screen.queryByRole('button', { name: 'Cancel recording' })).not.toBeInTheDocument()

    await new Promise((resolve) => setTimeout(resolve, 400))
    fireEvent.pointerUp(micButton)
    await waitFor(() => expect(transcribeCalls).toBe(1))
    // The transcript lands as editable draft text — deleting it (not a dedicated button) is
    // how an unwanted recording is discarded now.
    await waitFor(() =>
      expect(screen.getByPlaceholderText('Message Ask Lucy...')).toHaveValue('transcribed text'),
    )
  })

  it('collapsing the widget mid-recording discards it (FR-024) — never transcribed', async () => {
    // The collapse→cancel effect lives in ConversationView itself, keyed off its
    // `expanded` prop — driving that prop directly (via rerender) exercises the exact
    // same wiring `ChatAssistantWidget`'s real expand/collapse would, without needing the
    // full ChatPage tree (AiPresenceCard's Three.js scene doesn't coexist well with the
    // fake AudioContext/MediaRecorder globals this describe block installs).
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const { rerender } = render(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <ConversationView
            chatId={CHAT_A}
            language="en"
            onChatCreated={() => {}}
            onNewChat={() => {}}
            tts={mockTts}
            expanded
          />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    fireEvent.pointerDown(await findMicButton())
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Stop voice input' })).toBeInTheDocument(),
    )

    rerender(
      <MemoryRouter>
        <QueryClientProvider client={queryClient}>
          <ConversationView
            chatId={CHAT_A}
            language="en"
            onChatCreated={() => {}}
            onNewChat={() => {}}
            tts={mockTts}
            expanded={false}
          />
        </QueryClientProvider>
      </MemoryRouter>,
    )

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Expand Ask Lucy assistant' })).toBeInTheDocument(),
    )
    expect(transcribeCalls).toBe(0)
    // The Collapsed handle itself is idle, not stuck showing review controls.
    expect(screen.queryByRole('button', { name: 'Finished speaking' })).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Send recording for transcription' }),
    ).not.toBeInTheDocument()
  })

  it('Continuous Listening is completely unaffected — no recording-review UI ever appears (FR-025)', async () => {
    useVoicePreferencesStore.setState({ conversationMode: 'Continuous' })
    renderConversation(CHAT_A)

    // specs/029-fix-chat-widget-bugs research.md Decision 5: the old directly-clickable
    // "Switch to Push-to-Talk mode" button (VoiceControlBar, retired) is replaced by a
    // fixed-label settings trigger ("Voice input mode settings") that opens a menu — the
    // mode-dependent label now lives on the menu item, not the trigger button itself.
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'Voice input mode settings' }),
      ).toBeInTheDocument(),
    )
    expect(screen.queryByRole('button', { name: 'Finished speaking' })).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Send recording for transcription' }),
    ).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel recording' })).not.toBeInTheDocument()
    // specs/029-fix-chat-widget-bugs research.md Decision 5: ChatComposer is now the single
    // consolidated voice control (VoiceControlBar retired) — its one mic button always
    // renders regardless of conversationMode (FR-006), so exactly one match, in both modes.
    expect(screen.queryAllByRole('button', { name: 'Start voice input' })).toHaveLength(1)
  })

  // specs/034-transcription-crash-gesture-and-continuous-view FR-008 (resolved clarification)
  // — loading a chat with Continuous already saved as the mode preference must never
  // auto-open the dedicated voice view or start a live session; only an explicit action does.
  it('does not auto-open the dedicated voice view or start a session when Continuous is already the saved preference on load', async () => {
    // This mock is shared module-wide with no global mock-clearing convention in this file —
    // clear its own call history here so an unrelated earlier test's call doesn't false-fail
    // this assertion.
    conversationAudioMock.startTurn.mockClear()
    useVoicePreferencesStore.setState({ conversationMode: 'Continuous' })
    renderConversation(CHAT_A)

    await screen.findByPlaceholderText('Message Ask Lucy...')
    expect(screen.queryByRole('button', { name: 'Exit voice conversation' })).not.toBeInTheDocument()
    expect(conversationAudioMock.startTurn).not.toHaveBeenCalled()
  })

  // specs/031-voice-controls-redesign FR-009, specs/034-transcription-crash-gesture-and-
  // continuous-view (research.md Assumptions) — switching into Continuous mode now opens a
  // full-takeover dedicated voice view (the composer isn't visible at all while it's open, so
  // the original "still visible after switching" assertion no longer applies); draft text
  // must still survive the round trip once the user exits back to the normal view.
  it('preserves typed draft text across a conversation-mode switch, once back in the normal view (FR-009)', async () => {
    renderConversation(CHAT_A)

    const textbox = await screen.findByPlaceholderText('Message Ask Lucy...')
    fireEvent.change(textbox, { target: { value: 'Draft before switching modes' } })
    expect(textbox).toHaveValue('Draft before switching modes')

    // specs/032-transcription-and-mode-switch-fixes US2/FR-006 — a single click now toggles
    // the mode directly; the prior two-click dropdown menu is removed.
    fireEvent.click(screen.getByRole('button', { name: 'Voice input mode settings' }))

    await waitFor(() =>
      expect(useVoicePreferencesStore.getState().conversationMode).toBe('Continuous'),
    )
    // The dedicated voice view has taken over — no composer, only Exit/Mute.
    expect(screen.queryByPlaceholderText('Message Ask Lucy...')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Exit voice conversation' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Exit voice conversation' }))

    expect(await screen.findByPlaceholderText('Message Ask Lucy...')).toHaveValue(
      'Draft before switching modes',
    )
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
            onChatCreated={() => {}}
            onNewChat={() => {}}
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
            onChatCreated={() => {}}
            onNewChat={() => {}}
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
              onChatCreated={() => {}}
              onNewChat={() => {}}
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
