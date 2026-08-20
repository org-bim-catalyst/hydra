import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { axe, toHaveNoViolations } from 'jest-axe'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { MemoryRouter } from 'react-router'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { useWorkspaceOverlayStore } from '../../../store/workspaceOverlayStore'
import type { useVoiceOutput } from '../voice/useVoiceOutput'
import { useVoicePreferencesStore } from '../voice/voicePreferencesStore'
import { ChatPage, ConversationView } from './ChatPage'

const CHAT_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'

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

function renderConversation(chatId = CHAT_ID) {
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
          expanded
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
  // Note: ChatComposer.test.tsx already a11y-tests the muted, listening, and
  // permission-denied states directly against that component with deterministic props —
  // this integration-level check instead covers a state that's only reachable by the real,
  // page-level `voicePreferencesStore` (unlike `isMuted`, which the mocked `tts` here
  // doesn't reactively reflect): Continuous mode's real structural difference (the mic
  // button becomes a plain listening toggle rather than a Push-to-Talk hold trigger,
  // specs/029-fix-chat-widget-bugs research.md Decision 5 — VoiceControlBar retired).
  it('has no automatically detectable a11y violations in Continuous mode (listening status only)', async () => {
    server.use(
      http.get(`*/api/v1/chats/${CHAT_ID}/messages`, () =>
        HttpResponse.json({ items: [], nextCursor: null }),
      ),
    )
    useVoicePreferencesStore.setState({ conversationMode: 'Continuous' })
    const { container, findByLabelText } = renderConversation()

    await findByLabelText('Voice input mode settings')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

describe('ChatPage accessibility — chat panel (SPEC-006/SPEC-024, constitution §7/§10)', () => {
  beforeEach(() => {
    useWorkspaceOverlayStore.setState({
      expandedControlId: null,
      viewMode: 'isometric',
      unreadControlIds: new Set(),
    })
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

    fireEvent.click(await findByRole('button', { name: 'Expand Ask Lucy assistant' }))
    await findByText('Start a conversation with Ask Lucy.')

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations with the chat panel collapsed (default state) (FR-013/FR-014)', async () => {
    const { container, findByRole } = renderChatPage()

    await findByRole('button', { name: 'Expand Ask Lucy assistant' })

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})

describe('ChatPage accessibility — Studio workspace shell (SPEC-024, FR-019, constitution §7/§10)', () => {
  beforeEach(() => {
    useWorkspaceOverlayStore.setState({
      expandedControlId: null,
      viewMode: 'isometric',
      unreadControlIds: new Set(),
    })
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

describe('ConversationView accessibility — Push-to-Talk recording review (specs/026-floating-chat-assistant FR-019–FR-023, T052)', () => {
  // Full ChatPage-tree mounts are incompatible with these faked MediaRecorder/AudioContext
  // globals (AiPresenceCard's Three.js scene throws) — see the identical setup/rationale in
  // ChatPage.test.tsx's "Push-to-Talk recording review" describe block. ConversationView
  // rendered expanded exercises the exact same ExpandedChatPanel + VoiceControlBar DOM.
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
  const getMicButton = () => screen.getAllByRole('button', { name: 'Start voice input' })[0]

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
    server.use(
      http.get(`*/api/v1/chats/${CHAT_A}/messages`, () =>
        HttpResponse.json({ items: [], nextCursor: null }),
      ),
      http.post('*/api/v1/ai/transcriptions', () =>
        HttpResponse.json({ text: 'transcribed text' }),
      ),
    )
    vi.stubGlobal('AudioContext', FakeAudioContext)
    vi.stubGlobal('MediaRecorder', FakeMediaRecorder)
    Object.defineProperty(navigator, 'mediaDevices', {
      value: { getUserMedia: vi.fn(() => Promise.resolve(fakeStream)) },
      configurable: true,
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    Object.defineProperty(navigator, 'mediaDevices', { value: undefined, configurable: true })
  })

  it('has no automatically detectable a11y violations while recording (waveform, no live transcript)', async () => {
    const { container } = renderConversation(CHAT_A)

    fireEvent.click(getMicButton())
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Finished speaking' })).toBeInTheDocument(),
    )

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })

  it('has no automatically detectable a11y violations while reviewing before accept', async () => {
    const { container } = renderConversation(CHAT_A)

    fireEvent.click(getMicButton())
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Finished speaking' })).toBeInTheDocument(),
    )
    fireEvent.click(screen.getByRole('button', { name: 'Finished speaking' }))
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: 'Send recording for transcription' }),
      ).toBeInTheDocument(),
    )

    const results = await axe(container)
    expect(results).toHaveNoViolations()
  })
})
