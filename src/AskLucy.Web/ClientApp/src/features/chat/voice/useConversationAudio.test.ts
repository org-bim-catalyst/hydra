import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useVoiceProviderStatus } from './voiceProviderStatus'
import { useVoiceState } from './useVoiceState'

const recognitionMock = {
  isSupported: true,
  isListening: false,
  permissionState: 'unknown' as 'unknown' | 'granted' | 'denied',
  error: null as string | null,
  start: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn(),
  cancel: vi.fn(),
  clearError: vi.fn(),
}

let capturedOnFinalTranscript: ((text: string) => void) | undefined
let capturedOnLocalSpeechLikely: (() => void) | undefined

vi.mock('./useSpeechRecognition', () => ({
  useSpeechRecognition: vi.fn(
    (options: { onFinalTranscript: (text: string) => void; onLocalSpeechLikely?: () => void }) => {
      capturedOnFinalTranscript = options.onFinalTranscript
      capturedOnLocalSpeechLikely = options.onLocalSpeechLikely
      return recognitionMock
    },
  ),
}))

const speakMock = vi.fn()
const abortMock = vi.fn()

vi.mock('./useSpeechSynthesis', () => ({
  useSpeechSynthesis: vi.fn(() => ({
    isSpeaking: false,
    error: null,
    speak: speakMock,
    abort: abortMock,
    clearError: vi.fn(),
  })),
}))

const setMutedMock = vi.fn()
const analyzerResetMock = vi.fn()

vi.mock('./useVoiceAnalyzer', () => ({
  useVoiceAnalyzer: vi.fn(() => ({
    playAudioChunk: vi.fn(),
    endStream: vi.fn(),
    getReactiveIntensity: vi.fn(() => 0),
    setMuted: setMutedMock,
    reset: analyzerResetMock,
  })),
}))

vi.mock('./voiceProviderStatus', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./voiceProviderStatus')>()
  return { ...actual, probeRecoveryIfDegraded: vi.fn().mockResolvedValue(undefined) }
})

import { useConversationAudio } from './useConversationAudio'

describe('useConversationAudio', () => {
  beforeEach(() => {
    useVoiceProviderStatus.setState({ provider: 'primary', degradedNoticeVisible: false })
    useVoiceState.setState({ state: 'Idle', errorMessage: null })
    recognitionMock.permissionState = 'unknown'
    recognitionMock.start.mockClear()
    speakMock.mockReset()
    abortMock.mockReset()
    setMutedMock.mockReset()
    analyzerResetMock.mockReset()
    capturedOnFinalTranscript = undefined
    capturedOnLocalSpeechLikely = undefined
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  function renderConversationAudio(mode: 'push-to-talk' | 'continuous' = 'push-to-talk') {
    return renderHook(() =>
      useConversationAudio({
        chatId: 'chat-1',
        language: 'en',
        mode,
        providerId: 'provider-1',
        modelId: 'model-1',
        buildMessages: (transcript) => [{ role: 'user', content: transcript }],
        onUserTranscript: vi.fn(),
        onAssistantTextDelta: vi.fn(),
        onAssistantTurnComplete: vi.fn(),
      }),
    )
  }

  it('surfaces a visible, actionable error when the primary fails over and the fallback capture also fails (FR-032/FR-036)', async () => {
    useVoiceProviderStatus.setState({ provider: 'fallback', degradedNoticeVisible: true })
    recognitionMock.permissionState = 'denied'

    const { result } = renderConversationAudio()

    await act(async () => {
      await result.current.startTurn()
    })

    expect(result.current.voiceState).toBe('Error')
    expect(result.current.errorMessage).toContain('microphone was denied')
    expect(result.current.errorMessage).toContain('backup voice engine')
  })

  it('does not surface an error when only the primary has failed over but the fallback capture succeeds', async () => {
    useVoiceProviderStatus.setState({ provider: 'fallback', degradedNoticeVisible: true })
    recognitionMock.permissionState = 'granted'

    const { result } = renderConversationAudio()

    await act(async () => {
      await result.current.startTurn()
    })

    expect(result.current.voiceState).not.toBe('Error')
    expect(recognitionMock.start).toHaveBeenCalledTimes(1)
  })

  it('starts listening normally when the primary provider is healthy', async () => {
    recognitionMock.permissionState = 'granted'

    const { result } = renderConversationAudio()

    await act(async () => {
      await result.current.startTurn()
    })

    expect(result.current.voiceState).toBe('Listening')
    expect(result.current.errorMessage).toBeNull()
  })

  it('automatically resumes listening after the AI finishes speaking, in Continuous mode, with zero manual action (FR-014, SC-003)', async () => {
    recognitionMock.permissionState = 'granted'
    speakMock.mockImplementation(
      async (_chatId, _messages, _providerId, _modelId, _params, _language, callbacks) => {
        callbacks.onDone()
      },
    )

    renderConversationAudio('continuous')
    recognitionMock.start.mockClear()

    await act(async () => {
      capturedOnFinalTranscript?.('Hello there')
    })

    // The turn completed (onDone fired) and the loop auto-resumed listening — a second
    // recognition.start() call with no button/click involved.
    expect(recognitionMock.start).toHaveBeenCalledTimes(1)
  })

  it('returns to Idle (not Listening) after a Push-to-Talk turn completes — no auto-resume', async () => {
    recognitionMock.permissionState = 'granted'
    speakMock.mockImplementation(
      async (_chatId, _messages, _providerId, _modelId, _params, _language, callbacks) => {
        callbacks.onDone()
      },
    )

    const { result } = renderConversationAudio('push-to-talk')
    recognitionMock.start.mockClear()

    await act(async () => {
      capturedOnFinalTranscript?.('Hello there')
    })

    expect(recognitionMock.start).not.toHaveBeenCalled()
    expect(result.current.voiceState).toBe('Idle')
  })

  it('ducks playback immediately on a local speech pre-trigger during AiSpeaking, then fully interrupts once confirmed (US3, research.md Decision 10)', async () => {
    recognitionMock.permissionState = 'granted'
    let resolveSpeak: () => void = () => {}
    speakMock.mockImplementation(
      () =>
        new Promise<void>((resolve) => {
          resolveSpeak = resolve
        }),
    )

    const { result } = renderConversationAudio('push-to-talk')

    // Start a turn and let it reach AiSpeaking.
    await act(async () => {
      capturedOnFinalTranscript?.('First message')
    })
    act(() => useVoiceState.setState({ state: 'AiSpeaking', errorMessage: null }))

    // Local pre-trigger fires — must duck immediately, before any confirmation.
    act(() => {
      capturedOnLocalSpeechLikely?.()
    })
    expect(setMutedMock).toHaveBeenCalledWith(true)
    expect(result.current.voiceState).toBe('Interrupted')
    expect(abortMock).not.toHaveBeenCalled()

    // Confirmation arrives (a new final transcript) — now it fully interrupts.
    await act(async () => {
      capturedOnFinalTranscript?.('Actually, wait')
      resolveSpeak()
    })
    expect(abortMock).toHaveBeenCalledTimes(1)
    expect(analyzerResetMock).toHaveBeenCalled()
  })

  it('resumes playback un-muted if the local pre-trigger is a false positive (no confirming transcript)', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    recognitionMock.permissionState = 'granted'
    speakMock.mockImplementation(() => new Promise<void>(() => {}))

    const { result } = renderConversationAudio('push-to-talk')

    await act(async () => {
      capturedOnFinalTranscript?.('First message')
    })
    act(() => useVoiceState.setState({ state: 'AiSpeaking', errorMessage: null }))

    act(() => {
      capturedOnLocalSpeechLikely?.()
    })
    expect(setMutedMock).toHaveBeenCalledWith(true)
    expect(result.current.voiceState).toBe('Interrupted')

    setMutedMock.mockClear()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(1600)
    })

    expect(setMutedMock).toHaveBeenCalledWith(false)
    expect(result.current.voiceState).toBe('AiSpeaking')
    expect(abortMock).not.toHaveBeenCalled()

    vi.useRealTimers()
  })

  it('stop() cancels playback and generation, clears the audio queue, and resumes listening in Continuous mode (FR-023)', async () => {
    recognitionMock.permissionState = 'granted'
    speakMock.mockImplementation(() => new Promise<void>(() => {}))

    const { result } = renderConversationAudio('continuous')
    recognitionMock.start.mockClear()

    await act(async () => {
      capturedOnFinalTranscript?.('Tell me a long story')
    })
    act(() => useVoiceState.setState({ state: 'AiSpeaking', errorMessage: null }))

    await act(async () => {
      await result.current.stop()
    })

    expect(abortMock).toHaveBeenCalledTimes(1)
    expect(analyzerResetMock).toHaveBeenCalledTimes(1)
    expect(result.current.voiceState).toBe('Listening')
    expect(recognitionMock.start).toHaveBeenCalledTimes(1)
  })

  it('stop() resets to Idle, without resuming listening, in Push-to-Talk mode', async () => {
    recognitionMock.permissionState = 'granted'
    speakMock.mockImplementation(() => new Promise<void>(() => {}))

    const { result } = renderConversationAudio('push-to-talk')
    recognitionMock.start.mockClear()

    await act(async () => {
      capturedOnFinalTranscript?.('Tell me a long story')
    })
    act(() => useVoiceState.setState({ state: 'AiSpeaking', errorMessage: null }))

    await act(async () => {
      await result.current.stop()
    })

    expect(abortMock).toHaveBeenCalledTimes(1)
    expect(analyzerResetMock).toHaveBeenCalledTimes(1)
    expect(result.current.voiceState).toBe('Idle')
    expect(recognitionMock.start).not.toHaveBeenCalled()
  })
})
