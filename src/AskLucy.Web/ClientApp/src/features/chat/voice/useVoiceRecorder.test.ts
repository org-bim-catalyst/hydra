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

  it('buffers locally and never transmits anything through start()+finish() alone (FR-019/FR-020)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })
    expect(result.current.phase).toBe('recording')

    act(() => {
      result.current.finish()
    })

    expect(result.current.phase).toBe('reviewing')
    expect(transcribeAudio).not.toHaveBeenCalled()
  })

  it('accept() is the only path that ever calls transcribeAudio, and only after reviewing (FR-022)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    vi.mocked(transcribeAudio).mockResolvedValue('hello world')
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })
    act(() => {
      result.current.finish()
    })
    expect(transcribeAudio).not.toHaveBeenCalled()

    let transcript = ''
    await act(async () => {
      transcript = await result.current.accept()
    })

    expect(transcribeAudio).toHaveBeenCalledTimes(1)
    expect(transcript).toBe('hello world')
    expect(result.current.phase).toBe('idle')
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

  it('cancel() from the reviewing phase discards the buffered recording and never transmits (FR-021)', async () => {
    installAudioEnvironment(() => Promise.resolve(fakeStream))
    const { result } = renderHook(() => useVoiceRecorder())

    await act(async () => {
      await result.current.start()
    })
    act(() => {
      result.current.finish()
    })
    expect(result.current.phase).toBe('reviewing')

    act(() => {
      result.current.cancel()
    })

    expect(result.current.phase).toBe('idle')
    expect(transcribeAudio).not.toHaveBeenCalled()

    // accept() after a cancel is a no-op — nothing left to send.
    let transcript = 'unset'
    await act(async () => {
      transcript = await result.current.accept()
    })
    expect(transcript).toBe('')
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
