import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../api/aiApi', () => ({
  transcribeDictationClip: vi.fn(),
}))

vi.mock('../api/voiceApi', () => ({
  createSttSession: vi.fn(),
}))

vi.mock('./wavEncoder', () => ({
  toWav16kMono: vi.fn(),
}))

vi.mock('./dictationFallback', () => ({
  getBrowserSpeechRecognition: vi.fn(),
  startBrowserDictation: vi.fn(),
}))

import { transcribeDictationClip } from '../api/aiApi'
import { createSttSession } from '../api/voiceApi'
import { getBrowserSpeechRecognition, startBrowserDictation } from './dictationFallback'
import { useVoiceRecorder } from './useVoiceRecorder'
import { toWav16kMono } from './wavEncoder'

class FakeMediaRecorder {
  static instances: FakeMediaRecorder[] = []
  ondataavailable: ((event: { data: Blob }) => void) | null = null
  onstop: (() => void) | null = null
  mimeType = 'audio/webm'

  stream: MediaStream

  constructor(stream: MediaStream) {
    this.stream = stream
    FakeMediaRecorder.instances.push(this)
  }

  start = vi.fn(() => {
    // Simulates one chunk of captured audio arriving while recording.
    this.ondataavailable?.({ data: new Blob(['fake-audio'], { type: 'audio/webm' }) })
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
  getByteFrequencyData = vi.fn((data: Uint8Array) => data.fill(128))
}

class FakeAudioContext {
  createMediaStreamSource = vi.fn(() => ({ connect: vi.fn() }))
  createAnalyser = vi.fn(() => new FakeAnalyserNode())
  close = vi.fn().mockResolvedValue(undefined)
}

function installAudioEnvironment(getUserMediaImpl: () => Promise<MediaStream>) {
  FakeMediaRecorder.instances = []
  vi.stubGlobal('AudioContext', FakeAudioContext)
  vi.stubGlobal('MediaRecorder', FakeMediaRecorder)
  vi.stubGlobal('navigator', {
    mediaDevices: { getUserMedia: vi.fn(getUserMediaImpl) },
  })
}

let stopTrackMock: ReturnType<typeof vi.fn>
let fakeStream: MediaStream

describe('useVoiceRecorder (specs/026-floating-chat-assistant FR-019–FR-024, specs/078-restore-local-whisper)', () => {
  beforeEach(() => {
    vi.mocked(transcribeDictationClip).mockReset()
    vi.mocked(toWav16kMono).mockReset()
    vi.mocked(getBrowserSpeechRecognition).mockReset()
    vi.mocked(startBrowserDictation).mockReset()
    // contracts/dictation-session.md — the ordinary path unless a test overrides it: the
    // server resolves this Push-to-Talk turn to a recorded clip, nothing degraded.
    vi.mocked(createSttSession).mockResolvedValue({
      engine: 'Clip',
      token: null,
      expiresAtUtc: null,
      degraded: false,
    })
    stopTrackMock = vi.fn()
    fakeStream = { getTracks: () => [{ stop: stopTrackMock }] } as unknown as MediaStream
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('finish() stops capture, transcribes, and resolves to idle in one step (specs/031-voice-controls-redesign FR-001/FR-002)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(toWav16kMono).mockResolvedValue({ converted: true, blob: new Blob(['wav']) })
    vi.mocked(transcribeDictationClip).mockResolvedValue({ text: 'hello world', language: 'en' })
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })
    expect(result.current.phase).toBe('recording')
    expect(transcribeDictationClip).not.toHaveBeenCalled()

    let transcript = ''
    await act(async () => {
      transcript = await result.current.finish()
    })

    expect(transcribeDictationClip).toHaveBeenCalledTimes(1)
    expect(transcript).toBe('hello world')
    expect(result.current.phase).toBe('idle')
  })

  it('finish() called outside the recording phase is a no-op and never transcribes', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    const { result } = renderHook(() => useVoiceRecorder())

    let transcript = 'unset'
    await act(async () => {
      transcript = await result.current.finish()
    })

    expect(transcript).toBe('')
    expect(transcribeDictationClip).not.toHaveBeenCalled()
    expect(result.current.phase).toBe('idle')
  })

  it('a transcription failure shows the gentle-repeat message, calls onGentleRepeat, forces the next attempt to the browser built-in, and reverts on the attempt after that (specs/078 US2/FR-005b)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(toWav16kMono).mockResolvedValue({ converted: true, blob: new Blob(['wav']) })
    vi.mocked(transcribeDictationClip).mockRejectedValueOnce(new Error('Transcription failed with 500'))
    const onGentleRepeat = vi.fn()
    const { result } = renderHook(() => useVoiceRecorder(undefined, onGentleRepeat))

    await act(async () => {
      await result.current.start()
    })

    let transcript = 'unset'
    await act(async () => {
      transcript = await result.current.finish()
    })

    expect(transcript).toBe('')
    expect(result.current.phase).toBe('idle')
    expect(result.current.error).toBe('Sorry, I missed that — could you say it again?')
    expect(onGentleRepeat).toHaveBeenCalledWith('Sorry, I missed that — could you say it again?')

    // The very next attempt is forced straight to the browser built-in — no stt-session call.
    vi.mocked(createSttSession).mockClear()
    class FakeRecognitionCtor {}
    vi.mocked(getBrowserSpeechRecognition).mockReturnValue(
      FakeRecognitionCtor as unknown as ReturnType<typeof getBrowserSpeechRecognition>,
    )
    vi.mocked(startBrowserDictation).mockReturnValue({ commit: vi.fn(), cancel: vi.fn() })

    await act(async () => {
      await result.current.start()
    })

    expect(createSttSession).not.toHaveBeenCalled()
    expect(startBrowserDictation).toHaveBeenCalledTimes(1)

    // The attempt after that reverts to normal engine resolution.
    act(() => {
      result.current.cancel()
    })
    vi.mocked(createSttSession).mockResolvedValue({
      engine: 'Clip',
      token: null,
      expiresAtUtc: null,
      degraded: false,
    })

    await act(async () => {
      await result.current.start()
    })

    expect(createSttSession).toHaveBeenCalledTimes(1)
  })

  it('resolves the phase to idle without transcribing when the clip could not be converted to WAV', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(toWav16kMono).mockResolvedValue({ converted: false, blob: new Blob(['webm']), error: new Error('bad') })
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })

    let transcript = 'unset'
    await act(async () => {
      transcript = await result.current.finish()
    })

    expect(transcript).toBe('')
    expect(transcribeDictationClip).not.toHaveBeenCalled()
    expect(result.current.phase).toBe('idle')
    expect(result.current.engineNotice).toBe(
      "Dictation is unavailable — using your browser's speech recognition instead.",
    )
  })

  it('cancel() from the recording phase discards everything and never transmits (FR-021)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })
    expect(result.current.phase).toBe('recording')

    act(() => {
      result.current.cancel()
    })

    expect(result.current.phase).toBe('idle')
    expect(transcribeDictationClip).not.toHaveBeenCalled()
  })

  it('an externally-triggered cancel() (e.g. collapsing mid-recording) discards state just like a user-initiated one (FR-024)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })

    // Simulates ChatAssistantWidget calling cancel() on collapse, not a button the user clicked.
    act(() => {
      result.current.cancel()
    })

    expect(result.current.phase).toBe('idle')
    expect(transcribeDictationClip).not.toHaveBeenCalled()
    expect(stopTrackMock).toHaveBeenCalled()
  })

  it('surfaces a distinct permission-denied state without throwing (constitution §2.VIII)', async () => {
    installAudioEnvironment(() => Promise.reject(new DOMException('Denied', 'NotAllowedError')))
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })

    expect(result.current.phase).toBe('idle')
    expect(result.current.permissionState).toBe('denied')
    expect(result.current.error).toContain('Microphone access was denied')
  })

  it('fails closed rather than opening the microphone when the server resolves Realtime (FR-017)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockResolvedValue({
      engine: 'Realtime',
      token: 'tok-1',
      expiresAtUtc: new Date().toISOString(),
      degraded: false,
    })
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })

    expect(result.current.phase).toBe('idle')
    expect(result.current.error).toBe('Dictation is not available.')
    expect(FakeMediaRecorder.instances).toHaveLength(0)
  })

  it('falls back to the browser built-in the same way a Browser answer does when the session request itself fails (FR-005b)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockRejectedValue(new Error('unreachable'))
    class FakeRecognitionCtor {}
    vi.mocked(getBrowserSpeechRecognition).mockReturnValue(
      FakeRecognitionCtor as unknown as ReturnType<typeof getBrowserSpeechRecognition>,
    )
    vi.mocked(startBrowserDictation).mockReturnValue({ commit: vi.fn(), cancel: vi.fn() })

    const { result } = renderHook(() => useVoiceRecorder())
    await act(async () => {
      await result.current.start()
    })

    expect(result.current.phase).toBe('recording')
    expect(startBrowserDictation).toHaveBeenCalledTimes(1)
    expect(FakeMediaRecorder.instances).toHaveLength(0)
    expect(result.current.engineNotice).toBe(
      "Dictation is unavailable — using your browser's speech recognition instead.",
    )
  })

  it('records and transcribes through the browser recognizer when the server resolves Browser', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(createSttSession).mockResolvedValue({
      engine: 'Browser',
      token: null,
      expiresAtUtc: null,
      degraded: true,
    })
    class FakeRecognitionCtor {}
    vi.mocked(getBrowserSpeechRecognition).mockReturnValue(
      FakeRecognitionCtor as unknown as ReturnType<typeof getBrowserSpeechRecognition>,
    )
    const commitMock = vi.fn()
    const cancelMock = vi.fn()
    let onFinal: ((text: string) => void) | undefined
    vi.mocked(startBrowserDictation).mockImplementation((_Recognition, _language, callbacks) => {
      onFinal = callbacks.onFinal
      return { commit: commitMock, cancel: cancelMock }
    })

    const { result } = renderHook(() => useVoiceRecorder())
    await act(async () => {
      await result.current.start()
    })

    expect(result.current.phase).toBe('recording')
    expect(result.current.engineNotice).toBe(
      "Dictation is unavailable — using your browser's speech recognition instead.",
    )

    let transcript = ''
    await act(async () => {
      const promise = result.current.finish()
      onFinal?.('hello there')
      transcript = await promise
    })

    expect(commitMock).toHaveBeenCalledTimes(1)
    expect(transcribeDictationClip).not.toHaveBeenCalled()
    expect(transcript).toBe('hello there')
    expect(result.current.phase).toBe('idle')
  })
})
