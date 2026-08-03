import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useVoiceAnalyzer } from './useVoiceAnalyzer'

class FakeGainNode {
  gain = { value: 1 }
  connect = vi.fn()
}

class FakeAnalyserNode {
  fftSize = 0
  frequencyBinCount = 4
  connect = vi.fn()
  getByteFrequencyData = vi.fn((data: Uint8Array) => {
    data.fill(200)
  })
}

class FakeAudioContext {
  destination = {}
  createMediaElementSource = vi.fn(() => ({ connect: vi.fn() }))
  createAnalyser = vi.fn(() => new FakeAnalyserNode())
  createGain = vi.fn(() => new FakeGainNode())
  close = vi.fn().mockResolvedValue(undefined)
}

class FakeSourceBuffer extends EventTarget {
  static instances: FakeSourceBuffer[] = []
  updating = false
  appendBuffer = vi.fn()

  constructor() {
    super()
    FakeSourceBuffer.instances.push(this)
  }
}

class FakeMediaSource extends EventTarget {
  static instances: FakeMediaSource[] = []
  readyState = 'open'
  addSourceBuffer = vi.fn(() => new FakeSourceBuffer())
  endOfStream = vi.fn()

  constructor() {
    super()
    FakeMediaSource.instances.push(this)
  }
}

class FakeAudioElement extends EventTarget {
  static instances: FakeAudioElement[] = []
  autoplay = false
  src = ''
  error: { code: number } | null = null
  pause = vi.fn()
  removeAttribute = vi.fn()
  play = vi.fn().mockResolvedValue(undefined)

  constructor() {
    super()
    FakeAudioElement.instances.push(this)
  }
}

function installVoiceAnalyzerEnvironment() {
  FakeMediaSource.instances = []
  FakeAudioElement.instances = []
  FakeSourceBuffer.instances = []
  vi.stubGlobal('AudioContext', FakeAudioContext)
  vi.stubGlobal('MediaSource', FakeMediaSource)
  vi.stubGlobal('Audio', FakeAudioElement)
  vi.stubGlobal('URL', { ...URL, createObjectURL: vi.fn(() => 'blob:fake') })
}

describe('useVoiceAnalyzer', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('mutes only the audible output — getReactiveIntensity keeps reacting to the real signal regardless (FR-021)', () => {
    installVoiceAnalyzerEnvironment()
    const { result } = renderHook(() => useVoiceAnalyzer())

    act(() => {
      result.current.playAudioChunk('YWJj')
    })

    // The analyser sits upstream of the gain node (FR-021) — muting must not change what
    // getReactiveIntensity() computes.
    const intensityBeforeMute = result.current.getReactiveIntensity()
    expect(intensityBeforeMute).toBeGreaterThan(0)

    act(() => {
      result.current.setMuted(true)
    })

    const intensityAfterMute = result.current.getReactiveIntensity()
    expect(intensityAfterMute).toBe(intensityBeforeMute)
  })

  it('creates the audio graph lazily — only once the first chunk arrives, not on mount', () => {
    installVoiceAnalyzerEnvironment()
    renderHook(() => useVoiceAnalyzer())

    expect(FakeMediaSource.instances).toHaveLength(0)
  })

  it('appends chunks to the underlying MediaSource once the graph exists', () => {
    installVoiceAnalyzerEnvironment()
    const { result } = renderHook(() => useVoiceAnalyzer())

    act(() => {
      result.current.playAudioChunk('YWJj')
    })

    expect(FakeMediaSource.instances).toHaveLength(1)
  })

  it('reset() tears down the graph so a subsequent chunk builds a fresh one', () => {
    installVoiceAnalyzerEnvironment()
    const { result } = renderHook(() => useVoiceAnalyzer())

    act(() => {
      result.current.playAudioChunk('YWJj')
    })
    expect(FakeMediaSource.instances).toHaveLength(1)

    act(() => {
      result.current.reset()
    })

    act(() => {
      result.current.playAudioChunk('ZGVm')
    })
    expect(FakeMediaSource.instances).toHaveLength(2)
  })

  it('reports a playback error via onPlaybackError when audioElement.play() rejects (constitution §2.VIII — the passive autoplay attribute fails silently otherwise)', async () => {
    installVoiceAnalyzerEnvironment()
    const onPlaybackError = vi.fn()

    // Make every future FakeAudioElement's play() reject, before the hook creates one.
    const OriginalFakeAudioElement = FakeAudioElement
    class RejectingAudioElement extends OriginalFakeAudioElement {
      constructor() {
        super()
        this.play = vi.fn().mockRejectedValue(new Error('NotAllowedError'))
      }
    }
    vi.stubGlobal('Audio', RejectingAudioElement)

    const { result } = renderHook(() => useVoiceAnalyzer(onPlaybackError))

    act(() => {
      result.current.playAudioChunk('YWJj')
    })

    // play() is only attempted once the SourceBuffer's first append actually completes
    // (not immediately in ensureGraph — see the doc comment on startPlaybackOnce for why),
    // so drive 'sourceopen' then 'updateend' the same way a real browser would.
    act(() => {
      FakeMediaSource.instances[0].dispatchEvent(new Event('sourceopen'))
    })

    await act(async () => {
      FakeSourceBuffer.instances[0].dispatchEvent(new Event('updateend'))
      await Promise.resolve()
      await Promise.resolve()
    })

    expect(onPlaybackError).toHaveBeenCalledWith(expect.stringContaining('Playback failed to start'))
  })

  it('drains chunks queued before sourceopen fired, instead of deadlocking (draining otherwise only happens inside updateend, which never fires without a first appendBuffer call)', () => {
    installVoiceAnalyzerEnvironment()
    const { result } = renderHook(() => useVoiceAnalyzer())

    // Both chunks arrive before 'sourceopen' has fired — sourceBufferRef.current is still
    // null, so playAudioChunk queues both rather than appending either directly.
    act(() => {
      result.current.playAudioChunk('YWJj')
      result.current.playAudioChunk('ZGVm')
    })

    act(() => {
      FakeMediaSource.instances[0].dispatchEvent(new Event('sourceopen'))
    })

    const sourceBuffer = FakeSourceBuffer.instances[0]
    expect(sourceBuffer.appendBuffer).toHaveBeenCalledTimes(1)

    act(() => {
      sourceBuffer.dispatchEvent(new Event('updateend'))
    })

    expect(sourceBuffer.appendBuffer).toHaveBeenCalledTimes(2)
  })

  it('reports a playback error via onPlaybackError when the audio element fires a native error event', () => {
    installVoiceAnalyzerEnvironment()
    const onPlaybackError = vi.fn()
    const { result } = renderHook(() => useVoiceAnalyzer(onPlaybackError))

    act(() => {
      result.current.playAudioChunk('YWJj')
    })

    const audioElement = FakeAudioElement.instances[0]
    audioElement.error = { code: 3 }
    act(() => {
      audioElement.dispatchEvent(new Event('error'))
    })

    expect(onPlaybackError).toHaveBeenCalledWith(expect.stringContaining('code 3'))
  })
})
