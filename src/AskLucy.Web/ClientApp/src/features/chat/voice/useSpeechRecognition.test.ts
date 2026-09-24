import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useVoiceProviderStatus } from './voiceProviderStatus'

vi.mock('../api/voiceApi', () => ({
  createSttSession: vi.fn(),
}))

vi.mock('../api/aiApi', () => ({
  transcribeAudio: vi.fn(),
}))

import { ApiError } from '../../../api/httpClient'
import { transcribeAudio } from '../api/aiApi'
import { createSttSession } from '../api/voiceApi'
import { useSpeechRecognition } from './useSpeechRecognition'

class FakeWebSocket extends EventTarget {
  static instances: FakeWebSocket[] = []
  static readonly CONNECTING = 0
  static readonly OPEN = 1
  static readonly CLOSING = 2
  static readonly CLOSED = 3

  readyState = FakeWebSocket.CONNECTING
  sentMessages: string[] = []
  url: string

  constructor(url: string) {
    super()
    this.url = url
    FakeWebSocket.instances.push(this)
  }

  send(data: string) {
    this.sentMessages.push(data)
  }

  close() {
    this.readyState = FakeWebSocket.CLOSED
    this.dispatchEvent(new Event('close'))
  }

  triggerOpen() {
    this.readyState = FakeWebSocket.OPEN
    this.dispatchEvent(new Event('open'))
  }

  triggerError() {
    this.dispatchEvent(new Event('error'))
  }

  triggerMessage(payload: unknown) {
    this.dispatchEvent(new MessageEvent('message', { data: JSON.stringify(payload) }))
  }
}

class FakeAudioWorkletNode {
  static instances: FakeAudioWorkletNode[] = []

  port: {
    postMessage: ReturnType<typeof vi.fn>
    close: ReturnType<typeof vi.fn>
    onmessage: ((event: { data: Float32Array }) => void) | null
  } = {
    postMessage: vi.fn(),
    close: vi.fn(),
    onmessage: null,
  }

  disconnect = vi.fn()
  context: unknown
  name: string

  constructor(context: unknown, name: string) {
    this.context = context
    this.name = name
    FakeAudioWorkletNode.instances.push(this)
  }
}

/** The microphone's level as the fake analyser reports it — the Whisper fallback's pause detection reads it. */
let micLevel = 0

class FakeAudioContext {
  sampleRate = 48000
  audioWorklet = { addModule: vi.fn().mockResolvedValue(undefined) }
  createMediaStreamSource = vi.fn(() => ({ connect: vi.fn(), disconnect: vi.fn() }))
  createAnalyser = vi.fn(() => ({
    fftSize: 256,
    frequencyBinCount: 128,
    getByteFrequencyData: vi.fn(),
    getFloatTimeDomainData: vi.fn((samples: Float32Array) => samples.fill(micLevel)),
    connect: vi.fn(),
  }))
  close = vi.fn().mockResolvedValue(undefined)
}

function installAudioEnvironment(getUserMediaImpl: () => Promise<MediaStream>) {
  FakeWebSocket.instances = []
  FakeAudioWorkletNode.instances = []
  vi.stubGlobal('WebSocket', FakeWebSocket)
  vi.stubGlobal('AudioContext', FakeAudioContext)
  vi.stubGlobal('AudioWorkletNode', FakeAudioWorkletNode)
  vi.stubGlobal('navigator', {
    mediaDevices: {
      getUserMedia: vi.fn(getUserMediaImpl),
    },
  })
}

const fakeStream = { getTracks: () => [{ stop: vi.fn() }] } as unknown as MediaStream

class FakeMediaRecorder {
  static instances: FakeMediaRecorder[] = []
  state: 'inactive' | 'recording' = 'inactive'
  mimeType = 'audio/webm;codecs=opus'
  ondataavailable: ((event: { data: Blob }) => void) | null = null
  onstop: (() => void) | null = null

  constructor() {
    FakeMediaRecorder.instances.push(this)
  }

  start() {
    this.state = 'recording'
  }

  stop() {
    this.state = 'inactive'
    this.ondataavailable?.({ data: new Blob(['audio']) })
    this.onstop?.()
  }
}

class FakeSpeechRecognition {
  static instances: FakeSpeechRecognition[] = []
  lang = ''
  continuous = true
  interimResults = false
  onresult: ((event: unknown) => void) | null = null
  onerror: ((event: { error: string }) => void) | null = null
  onend: (() => void) | null = null
  start = vi.fn()
  stop = vi.fn(() => this.onend?.())
  abort = vi.fn()

  constructor() {
    FakeSpeechRecognition.instances.push(this)
  }

  emit(text: string, isFinal: boolean) {
    this.onresult?.({ results: [Object.assign([{ transcript: text }], { isFinal })] })
  }
}

function installFallbackEngines({ whisper, browser }: { whisper: boolean; browser: boolean }) {
  FakeMediaRecorder.instances = []
  FakeSpeechRecognition.instances = []
  if (whisper) vi.stubGlobal('MediaRecorder', FakeMediaRecorder)
  if (browser) vi.stubGlobal('SpeechRecognition', FakeSpeechRecognition)
}

const refusedByServer = () => new ApiError(502, 'ElevenLabs is switched off under Admin → AI providers.')

describe('useSpeechRecognition', () => {
  beforeEach(() => {
    useVoiceProviderStatus.setState({ provider: 'primary', degradedNoticeVisible: false })
    vi.mocked(createSttSession).mockReset()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('connects on the first attempt and starts listening (FR-001/FR-002)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockResolvedValue({
      token: 'tok-1',
      expiresAtUtc: new Date().toISOString(),
    })

    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'push-to-talk',
        onPartialTranscript: vi.fn(),
        onFinalTranscript: vi.fn(),
      }),
    )

    await act(async () => {
      const promise = result.current.start()
      await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1))
      FakeWebSocket.instances[0].triggerOpen()
      await promise
    })

    expect(result.current.isListening).toBe(true)
    expect(result.current.permissionState).toBe('granted')
    expect(useVoiceProviderStatus.getState().provider).toBe('primary')
  })

  it('reconnects transparently within the retry budget without failing over (research.md Decision 8, FR-004)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockResolvedValue({
      token: 'tok-1',
      expiresAtUtc: new Date().toISOString(),
    })
    vi.useFakeTimers({ shouldAdvanceTime: true })

    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'push-to-talk',
        onPartialTranscript: vi.fn(),
        onFinalTranscript: vi.fn(),
      }),
    )

    const startPromise = act(async () => {
      const promise = result.current.start()
      // First connection attempt fails immediately.
      await vi.waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1))
      FakeWebSocket.instances[0].triggerError()
      await vi.advanceTimersByTimeAsync(1000)
      // Retry succeeds.
      await vi.waitFor(() => expect(FakeWebSocket.instances).toHaveLength(2))
      FakeWebSocket.instances[1].triggerOpen()
      await promise
    })

    await startPromise

    expect(useVoiceProviderStatus.getState().provider).toBe('primary')
    expect(result.current.isListening).toBe(true)
  })

  it('fails over to the fallback engine once the retry budget is exhausted (research.md Decision 8, FR-033)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockResolvedValue({
      token: 'tok-1',
      expiresAtUtc: new Date().toISOString(),
    })
    vi.useFakeTimers({ shouldAdvanceTime: true })

    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'push-to-talk',
        onPartialTranscript: vi.fn(),
        onFinalTranscript: vi.fn(),
      }),
    )

    await act(async () => {
      const promise = result.current.start()

      for (let attempt = 0; attempt < 3; attempt++) {
        await vi.waitFor(() => expect(FakeWebSocket.instances).toHaveLength(attempt + 1))
        FakeWebSocket.instances[attempt].triggerError()
        if (attempt < 2) await vi.advanceTimersByTimeAsync(1000)
      }

      await promise
    })

    expect(useVoiceProviderStatus.getState().provider).toBe('fallback')
    expect(useVoiceProviderStatus.getState().degradedNoticeVisible).toBe(true)
    expect(result.current.isListening).toBe(false)
  })

  it('surfaces a distinct permission-required state when the microphone is denied (FR-003)', async () => {
    installAudioEnvironment(() => Promise.reject(new DOMException('Denied', 'NotAllowedError')))

    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'push-to-talk',
        onPartialTranscript: vi.fn(),
        onFinalTranscript: vi.fn(),
      }),
    )

    await act(async () => {
      await result.current.start()
    })

    expect(result.current.permissionState).toBe('denied')
    expect(result.current.error).toContain('Microphone access was denied')
  })

  it('surfaces partial and committed transcripts from the WebSocket (FR-001/FR-002)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockResolvedValue({
      token: 'tok-1',
      expiresAtUtc: new Date().toISOString(),
    })

    const onPartialTranscript = vi.fn()
    const onFinalTranscript = vi.fn()
    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'push-to-talk',
        onPartialTranscript,
        onFinalTranscript,
      }),
    )

    await act(async () => {
      const promise = result.current.start()
      await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1))
      FakeWebSocket.instances[0].triggerOpen()
      await promise
    })

    act(() => {
      FakeWebSocket.instances[0].triggerMessage({
        message_type: 'partial_transcript',
        text: 'Hello',
      })
    })
    expect(onPartialTranscript).toHaveBeenCalledWith('Hello')

    act(() => {
      FakeWebSocket.instances[0].triggerMessage({
        message_type: 'committed_transcript',
        text: 'Hello there',
      })
    })
    expect(onFinalTranscript).toHaveBeenCalledWith('Hello there')
  })

  it('sends outbound messages in ElevenLabs\' verified realtime STT wire format (SPEC-013 T010)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockResolvedValue({
      token: 'tok-1',
      expiresAtUtc: new Date().toISOString(),
    })

    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'push-to-talk',
        onPartialTranscript: vi.fn(),
        onFinalTranscript: vi.fn(),
      }),
    )

    await act(async () => {
      const promise = result.current.start()
      await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1))
      FakeWebSocket.instances[0].triggerOpen()
      await promise
    })

    const socket = FakeWebSocket.instances[0]
    expect(socket.url).toContain('model_id=scribe_v2_realtime')
    expect(socket.url).toContain('audio_format=pcm_16000')

    const worklet = FakeAudioWorkletNode.instances[0]
    act(() => {
      worklet.port.onmessage?.({ data: new Float32Array([0.1, -0.1, 0.1]) })
    })

    const audioChunkMessage = JSON.parse(socket.sentMessages[0]) as Record<string, unknown>
    expect(audioChunkMessage).toMatchObject({
      message_type: 'input_audio_chunk',
      sample_rate: 16000,
      commit: false,
    })
    expect(typeof audioChunkMessage.audio_base_64).toBe('string')
    expect(audioChunkMessage).not.toHaveProperty('audio')
    expect(audioChunkMessage).not.toHaveProperty('type')

    act(() => {
      result.current.stop()
    })

    const commitMessage = JSON.parse(
      socket.sentMessages[socket.sentMessages.length - 1],
    ) as Record<string, unknown>
    expect(commitMessage).toMatchObject({ message_type: 'input_audio_chunk', commit: true })
  })

  it('auto-commits after a silence pause in continuous mode, without waiting for a manual stop (FR-014)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockResolvedValue({
      token: 'tok-1',
      expiresAtUtc: new Date().toISOString(),
    })
    vi.useFakeTimers({ shouldAdvanceTime: true })

    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'continuous',
        onPartialTranscript: vi.fn(),
        onFinalTranscript: vi.fn(),
      }),
    )

    await act(async () => {
      const promise = result.current.start()
      await vi.waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1))
      FakeWebSocket.instances[0].triggerOpen()
      await promise
    })

    const socket = FakeWebSocket.instances[0]
    act(() => {
      socket.triggerMessage({ message_type: 'partial_transcript', text: 'Hello' })
    })

    // No commit yet — still within the silence window.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(500)
    })
    expect(
      socket.sentMessages.some((m) => JSON.parse(m).message_type === 'input_audio_chunk' && JSON.parse(m).commit === true),
    ).toBe(false)

    // Silence persists past the threshold — auto-commits without any manual action.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(400)
    })
    expect(
      socket.sentMessages.some((m) => JSON.parse(m).message_type === 'input_audio_chunk' && JSON.parse(m).commit === true),
    ).toBe(true)
  })

  it('resets the silence timer on each new partial transcript, in continuous mode (FR-005)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockResolvedValue({
      token: 'tok-1',
      expiresAtUtc: new Date().toISOString(),
    })
    vi.useFakeTimers({ shouldAdvanceTime: true })

    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'continuous',
        onPartialTranscript: vi.fn(),
        onFinalTranscript: vi.fn(),
      }),
    )

    await act(async () => {
      const promise = result.current.start()
      await vi.waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1))
      FakeWebSocket.instances[0].triggerOpen()
      await promise
    })

    const socket = FakeWebSocket.instances[0]
    act(() => {
      socket.triggerMessage({ message_type: 'partial_transcript', text: 'Hello' })
    })

    await act(async () => {
      await vi.advanceTimersByTimeAsync(700)
    })

    // A fresh partial arrives just before the 800ms threshold — the timer must restart.
    act(() => {
      socket.triggerMessage({ message_type: 'partial_transcript', text: 'Hello there' })
    })

    await act(async () => {
      await vi.advanceTimersByTimeAsync(700)
    })
    expect(
      socket.sentMessages.some((m) => JSON.parse(m).message_type === 'input_audio_chunk' && JSON.parse(m).commit === true),
    ).toBe(false)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(200)
    })
    expect(
      socket.sentMessages.some((m) => JSON.parse(m).message_type === 'input_audio_chunk' && JSON.parse(m).commit === true),
    ).toBe(true)
  })

  // specs/033-hold-to-talk-and-echo-fix FR-009: replaces the removed local-speech-pre-trigger/
  // ducking mechanism (research.md Decision 10) with a direct mute of the input track — the
  // now-superseded interruption feature that mechanism existed for is gone entirely, not just
  // disabled.
  it('setInputMuted(true) disables the active stream\'s audio tracks, and setInputMuted(false) re-enables them', async () => {
    const track = { enabled: true, stop: vi.fn() }
    const streamWithAudioTrack = {
      getTracks: () => [track],
      getAudioTracks: () => [track],
    } as unknown as MediaStream
    installAudioEnvironment(() => Promise.resolve(streamWithAudioTrack))
    vi.mocked(createSttSession).mockResolvedValue({
      token: 'tok-1',
      expiresAtUtc: new Date().toISOString(),
    })

    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'push-to-talk',
        onPartialTranscript: vi.fn(),
        onFinalTranscript: vi.fn(),
      }),
    )

    await act(async () => {
      const promise = result.current.start()
      await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1))
      FakeWebSocket.instances[0].triggerOpen()
      await promise
    })

    act(() => result.current.setInputMuted(true))
    expect(track.enabled).toBe(false)

    act(() => result.current.setInputMuted(false))
    expect(track.enabled).toBe(true)
  })

  it('setInputMuted is a safe no-op when no stream is active', () => {
    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'push-to-talk',
        onPartialTranscript: vi.fn(),
        onFinalTranscript: vi.fn(),
      }),
    )

    expect(() => result.current.setInputMuted(true)).not.toThrow()
  })

  describe('when ElevenLabs is unavailable', () => {
    beforeEach(() => {
      micLevel = 0
      vi.mocked(transcribeAudio).mockReset()
    })

    function renderRecognition(overrides: { onError?: (message: string) => void } = {}) {
      const onPartialTranscript = vi.fn()
      const onFinalTranscript = vi.fn()
      const rendered = renderHook(() =>
        useSpeechRecognition({
          language: 'en',
          mode: 'continuous',
          onPartialTranscript,
          onFinalTranscript,
          ...overrides,
        }),
      )
      return { ...rendered, onPartialTranscript, onFinalTranscript }
    }

    it('does not retry a session the server refused, and dictates through Whisper instead', async () => {
      installAudioEnvironment(() => Promise.resolve(fakeStream))
      installFallbackEngines({ whisper: true, browser: true })
      vi.mocked(createSttSession).mockRejectedValue(refusedByServer())

      const { result } = renderRecognition()
      await act(async () => {
        await result.current.start()
      })

      expect(createSttSession).toHaveBeenCalledTimes(1)
      expect(FakeWebSocket.instances).toHaveLength(0)
      expect(useVoiceProviderStatus.getState().provider).toBe('fallback')
      expect(FakeMediaRecorder.instances).toHaveLength(1)
      expect(FakeSpeechRecognition.instances).toHaveLength(0)
      expect(result.current.isListening).toBe(true)
      expect(result.current.engineNotice).toContain('Whisper')
      expect(result.current.error).toBeNull()
    })

    it('goes straight to the fallback once failed over, without asking for a session again', async () => {
      useVoiceProviderStatus.setState({ provider: 'fallback', degradedNoticeVisible: true })
      installAudioEnvironment(() => Promise.resolve(fakeStream))
      installFallbackEngines({ whisper: true, browser: false })

      const { result } = renderRecognition()
      await act(async () => {
        await result.current.start()
      })

      expect(createSttSession).not.toHaveBeenCalled()
      expect(FakeMediaRecorder.instances).toHaveLength(1)
      expect(result.current.isListening).toBe(true)
    })

    it('transcribes an utterance with Whisper once the speaker pauses', async () => {
      installAudioEnvironment(() => Promise.resolve(fakeStream))
      installFallbackEngines({ whisper: true, browser: false })
      vi.mocked(createSttSession).mockRejectedValue(refusedByServer())
      vi.mocked(transcribeAudio).mockResolvedValue('  hello there  ')
      vi.useFakeTimers()

      const { result, onPartialTranscript, onFinalTranscript } = renderRecognition()
      await act(async () => {
        await result.current.start()
      })

      micLevel = 0.2
      await act(async () => {
        await vi.advanceTimersByTimeAsync(500)
      })
      expect(onPartialTranscript).toHaveBeenCalledWith('')
      expect(transcribeAudio).not.toHaveBeenCalled()

      micLevel = 0
      await act(async () => {
        await vi.advanceTimersByTimeAsync(1300)
      })

      expect(transcribeAudio).toHaveBeenCalledTimes(1)
      expect(vi.mocked(transcribeAudio).mock.calls[0][0].name).toBe('dictation.webm')
      expect(onFinalTranscript).toHaveBeenCalledWith('hello there')
      expect(result.current.isListening).toBe(false)
    })

    it('never uploads a recording in which nobody spoke', async () => {
      installAudioEnvironment(() => Promise.resolve(fakeStream))
      installFallbackEngines({ whisper: true, browser: false })
      vi.mocked(createSttSession).mockRejectedValue(refusedByServer())
      vi.useFakeTimers()

      const { result } = renderRecognition()
      await act(async () => {
        await result.current.start()
      })
      await act(async () => {
        await vi.advanceTimersByTimeAsync(31_000)
      })

      expect(transcribeAudio).not.toHaveBeenCalled()
      // The idle recording was restarted rather than left to grow.
      expect(FakeMediaRecorder.instances).toHaveLength(2)
      expect(result.current.isListening).toBe(true)
    })

    it("surfaces a Whisper failure and switches later turns to the browser's recognizer", async () => {
      installAudioEnvironment(() => Promise.resolve(fakeStream))
      installFallbackEngines({ whisper: true, browser: true })
      vi.mocked(createSttSession).mockRejectedValue(refusedByServer())
      vi.mocked(transcribeAudio).mockRejectedValue(new ApiError(502, 'Transcription is not configured.'))
      vi.useFakeTimers()
      const onError = vi.fn()

      const { result, onFinalTranscript } = renderRecognition({ onError })
      await act(async () => {
        await result.current.start()
      })
      micLevel = 0.2
      await act(async () => {
        await vi.advanceTimersByTimeAsync(200)
      })
      micLevel = 0
      await act(async () => {
        await vi.advanceTimersByTimeAsync(1300)
      })

      const message = "Transcription is not configured. Dictation will use your browser's speech recognition from your next turn."
      expect(result.current.error).toBe(message)
      expect(onError).toHaveBeenCalledWith(message)
      expect(onFinalTranscript).not.toHaveBeenCalled()
      expect(result.current.isListening).toBe(false)

      await act(async () => {
        await result.current.start()
      })
      expect(FakeSpeechRecognition.instances).toHaveLength(1)
      expect(result.current.engineNotice).toContain("browser's speech recognition")
    })

    it("dictates through the browser's recognizer when Whisper can't record here", async () => {
      installAudioEnvironment(() => Promise.resolve(fakeStream))
      installFallbackEngines({ whisper: false, browser: true })
      vi.mocked(createSttSession).mockRejectedValue(refusedByServer())

      const { result, onPartialTranscript, onFinalTranscript } = renderRecognition()
      await act(async () => {
        await result.current.start()
      })
      const recognizer = FakeSpeechRecognition.instances[0]
      expect(recognizer.lang).toBe('en')
      expect(recognizer.interimResults).toBe(true)

      act(() => recognizer.emit('hel', false))
      expect(onPartialTranscript).toHaveBeenCalledWith('hel')

      // Silence ends the browser's session without a result — it listens again rather than stopping.
      act(() => {
        recognizer.onerror?.({ error: 'no-speech' })
        recognizer.onend?.()
      })
      expect(recognizer.start).toHaveBeenCalledTimes(2)
      expect(result.current.error).toBeNull()

      act(() => recognizer.emit('hello', true))
      expect(onFinalTranscript).toHaveBeenCalledWith('hello')
      expect(result.current.isListening).toBe(false)
    })

    it("surfaces the browser recognizer's own failure", async () => {
      installAudioEnvironment(() => Promise.resolve(fakeStream))
      installFallbackEngines({ whisper: false, browser: true })
      vi.mocked(createSttSession).mockRejectedValue(refusedByServer())
      const onError = vi.fn()

      const { result } = renderRecognition({ onError })
      await act(async () => {
        await result.current.start()
      })
      act(() => FakeSpeechRecognition.instances[0].onerror?.({ error: 'network' }))

      expect(onError).toHaveBeenCalledWith(expect.stringContaining("couldn't reach its service"))
      expect(result.current.isListening).toBe(false)
    })

    it('says so when no engine at all can take over', async () => {
      installAudioEnvironment(() => Promise.resolve(fakeStream))
      installFallbackEngines({ whisper: false, browser: false })
      vi.mocked(createSttSession).mockRejectedValue(refusedByServer())
      const onError = vi.fn()

      const { result } = renderRecognition({ onError })
      await act(async () => {
        await result.current.start()
      })

      expect(result.current.error).toContain('Live dictation is unavailable')
      expect(onError).toHaveBeenCalledWith(expect.stringContaining('Live dictation is unavailable'))
      expect(result.current.isListening).toBe(false)
    })
  })
})
