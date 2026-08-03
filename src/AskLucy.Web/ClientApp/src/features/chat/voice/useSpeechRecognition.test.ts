import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useVoiceProviderStatus } from './voiceProviderStatus'

vi.mock('../api/voiceApi', () => ({
  createSttSession: vi.fn(),
}))

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

class FakeAudioContext {
  sampleRate = 48000
  audioWorklet = { addModule: vi.fn().mockResolvedValue(undefined) }
  createMediaStreamSource = vi.fn(() => ({ connect: vi.fn(), disconnect: vi.fn() }))
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
      FakeWebSocket.instances[0].triggerMessage({ type: 'partial_transcript', text: 'Hello' })
    })
    expect(onPartialTranscript).toHaveBeenCalledWith('Hello')

    act(() => {
      FakeWebSocket.instances[0].triggerMessage({
        type: 'committed_transcript',
        text: 'Hello there',
      })
    })
    expect(onFinalTranscript).toHaveBeenCalledWith('Hello there')
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
      socket.triggerMessage({ type: 'partial_transcript', text: 'Hello' })
    })

    // No commit yet — still within the silence window.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(500)
    })
    expect(socket.sentMessages.some((m) => JSON.parse(m).type === 'commit')).toBe(false)

    // Silence persists past the threshold — auto-commits without any manual action.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(400)
    })
    expect(socket.sentMessages.some((m) => JSON.parse(m).type === 'commit')).toBe(true)
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
      socket.triggerMessage({ type: 'partial_transcript', text: 'Hello' })
    })

    await act(async () => {
      await vi.advanceTimersByTimeAsync(700)
    })

    // A fresh partial arrives just before the 800ms threshold — the timer must restart.
    act(() => {
      socket.triggerMessage({ type: 'partial_transcript', text: 'Hello there' })
    })

    await act(async () => {
      await vi.advanceTimersByTimeAsync(700)
    })
    expect(socket.sentMessages.some((m) => JSON.parse(m).type === 'commit')).toBe(false)

    await act(async () => {
      await vi.advanceTimersByTimeAsync(200)
    })
    expect(socket.sentMessages.some((m) => JSON.parse(m).type === 'commit')).toBe(true)
  })

  it('fires the local speech pre-trigger on a loud audio chunk, but not on a quiet one (research.md Decision 10)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockResolvedValue({
      token: 'tok-1',
      expiresAtUtc: new Date().toISOString(),
    })

    const onLocalSpeechLikely = vi.fn()
    const { result } = renderHook(() =>
      useSpeechRecognition({
        language: 'en',
        mode: 'push-to-talk',
        onPartialTranscript: vi.fn(),
        onFinalTranscript: vi.fn(),
        onLocalSpeechLikely,
      }),
    )

    await act(async () => {
      const promise = result.current.start()
      await waitFor(() => expect(FakeWebSocket.instances).toHaveLength(1))
      FakeWebSocket.instances[0].triggerOpen()
      await promise
    })

    const worklet = FakeAudioWorkletNode.instances[0]

    act(() => {
      worklet.port.onmessage?.({ data: new Float32Array([0.001, -0.002, 0.001]) })
    })
    expect(onLocalSpeechLikely).not.toHaveBeenCalled()

    act(() => {
      worklet.port.onmessage?.({ data: new Float32Array([0.01, 0.5, -0.1]) })
    })
    expect(onLocalSpeechLikely).toHaveBeenCalledTimes(1)
  })
})
