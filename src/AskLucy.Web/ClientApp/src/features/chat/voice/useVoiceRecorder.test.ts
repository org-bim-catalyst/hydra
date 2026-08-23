import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('../api/aiApi', () => ({
  transcribeAudio: vi.fn(),
}))

import { transcribeAudio } from '../api/aiApi'
import { useVoiceRecorder } from './useVoiceRecorder'

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

describe('useVoiceRecorder (specs/026-floating-chat-assistant FR-019–FR-024)', () => {
  beforeEach(() => {
    vi.mocked(transcribeAudio).mockReset()
    stopTrackMock = vi.fn()
    fakeStream = { getTracks: () => [{ stop: stopTrackMock }] } as unknown as MediaStream
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('finish() stops capture, transcribes, and resolves to idle in one step (specs/031-voice-controls-redesign FR-001/FR-002)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(transcribeAudio).mockResolvedValue('hello world')
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })
    expect(result.current.phase).toBe('recording')
    expect(transcribeAudio).not.toHaveBeenCalled()

    let transcript = ''
    await act(async () => {
      transcript = await result.current.finish()
    })

    expect(transcribeAudio).toHaveBeenCalledTimes(1)
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
    expect(transcribeAudio).not.toHaveBeenCalled()
    expect(result.current.phase).toBe('idle')
  })

  it('a transcribeAudio failure surfaces via error and still resolves the phase to idle (FR-015)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(transcribeAudio).mockRejectedValue(new Error('Transcription failed with 500'))
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })

    let transcript = 'unset'
    await act(async () => {
      transcript = await result.current.finish()
    })

    expect(transcript).toBe('')
    expect(result.current.phase).toBe('idle')
    expect(result.current.error).toBe('Transcription failed with 500')
  })

  // specs/032 T006 (U1): before this fix the uploaded filename was hardcoded to
  // 'recording.webm' regardless of the browser's actual MediaRecorder mimeType — a
  // concrete, code-identified trigger for OpenAI rejecting the upload with a 400.
  it.each([
    ['audio/webm', 'recording.webm'],
    ['audio/webm;codecs=opus', 'recording.webm'],
    ['audio/mp4', 'recording.mp4'],
    ['audio/mp4;codecs=mp4a.40.2', 'recording.mp4'],
    ['audio/ogg;codecs=opus', 'recording.ogg'],
    ['audio/wav', 'recording.wav'],
    ['audio/mpeg', 'recording.mp3'],
    ['audio/x-made-up-format', 'recording.webm'],
  ])('finish() names the uploaded file to match the recorded mimeType %s -> %s', async (mimeType, expectedFileName) => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(transcribeAudio).mockResolvedValue('hello world')
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })
    FakeMediaRecorder.instances[0].mimeType = mimeType

    await act(async () => {
      await result.current.finish()
    })

    const uploadedFile = vi.mocked(transcribeAudio).mock.calls[0][0]
    expect(uploadedFile.name).toBe(expectedFileName)
    expect(uploadedFile.type).toBe(mimeType)
  })

  it('a rejected transcription surfaces the ApiError message (the Problem Details detail), not a generic string', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(transcribeAudio).mockRejectedValue(
      new Error('The AI provider could not process this request. Please try again.'),
    )
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })

    await act(async () => {
      await result.current.finish()
    })

    expect(result.current.error).toBe('The AI provider could not process this request. Please try again.')
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
    expect(transcribeAudio).not.toHaveBeenCalled()
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
    expect(transcribeAudio).not.toHaveBeenCalled()
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
})
