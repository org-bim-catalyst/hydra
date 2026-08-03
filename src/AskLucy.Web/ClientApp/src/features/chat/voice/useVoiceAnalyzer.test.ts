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
  updating = false
  appendBuffer = vi.fn()
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

class FakeAudioElement {
  autoplay = false
  src = ''
  pause = vi.fn()
  removeAttribute = vi.fn()
}

function installVoiceAnalyzerEnvironment() {
  FakeMediaSource.instances = []
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
})
