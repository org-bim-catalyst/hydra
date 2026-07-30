import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useTextToSpeech } from './useTextToSpeech'

interface FakeUtteranceInstance {
  text: string
  lang: string
  voice: unknown
  onstart: (() => void) | null
  onboundary: (() => void) | null
  onend: (() => void) | null
  onerror: (() => void) | null
}

function installSpeechSynthesis() {
  const instances: FakeUtteranceInstance[] = []

  class FakeUtterance implements FakeUtteranceInstance {
    text: string
    lang = ''
    voice: unknown = null
    onstart: (() => void) | null = null
    onboundary: (() => void) | null = null
    onend: (() => void) | null = null
    onerror: (() => void) | null = null
    constructor(text: string) {
      this.text = text
      instances.push(this)
    }
  }

  const speak = vi.fn()
  const cancel = vi.fn()
  vi.stubGlobal('SpeechSynthesisUtterance', FakeUtterance)
  vi.stubGlobal('speechSynthesis', { speak, cancel, getVoices: () => [] })

  return { instances, speak, cancel }
}

describe('useTextToSpeech', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('isSupported reflects whether window.speechSynthesis exists', () => {
    installSpeechSynthesis()
    const { result } = renderHook(() => useTextToSpeech())
    expect(result.current.isSupported).toBe(true)
  })

  it('surfaces a caller-visible error and skips speaking when unsupported (constitution §2.VIII)', () => {
    const { result } = renderHook(() => useTextToSpeech())

    act(() => result.current.speak('hello', 'en'))

    expect(result.current.error).toBe('Voice output is not supported in this browser.')
  })

  it('tracks isSpeaking across the utterance lifecycle', () => {
    const { instances } = installSpeechSynthesis()
    const { result } = renderHook(() => useTextToSpeech())

    act(() => result.current.speak('hello', 'en'))
    expect(result.current.isSpeaking).toBe(false)

    act(() => instances[0].onstart?.())
    expect(result.current.isSpeaking).toBe(true)

    act(() => instances[0].onend?.())
    expect(result.current.isSpeaking).toBe(false)
  })

  it('pulses getIntensity to 1 on a boundary event and decays it back toward 0 (FR-018)', async () => {
    const { instances } = installSpeechSynthesis()
    const { result } = renderHook(() => useTextToSpeech())

    act(() => result.current.speak('hello there', 'en'))
    expect(result.current.getIntensity()).toBe(0)

    act(() => instances[0].onboundary?.())
    expect(result.current.getIntensity()).toBe(1)

    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 200))
    })

    expect(result.current.getIntensity()).toBeLessThan(1)
  })

  it('onerror surfaces a caller-visible failure and resets speaking/intensity (constitution §2.VIII, FR-019)', () => {
    const { instances } = installSpeechSynthesis()
    const { result } = renderHook(() => useTextToSpeech())

    act(() => result.current.speak('hello', 'en'))
    act(() => instances[0].onstart?.())
    act(() => instances[0].onerror?.())

    expect(result.current.isSpeaking).toBe(false)
    expect(result.current.getIntensity()).toBe(0)
    expect(result.current.error).toBe('Voice output failed. Please try again.')
  })

  it('clearError resets the error state', () => {
    const { result } = renderHook(() => useTextToSpeech())
    act(() => result.current.speak('hello', 'en'))
    expect(result.current.error).not.toBeNull()

    act(() => result.current.clearError())
    expect(result.current.error).toBeNull()
  })

  it('stop() cancels speech and resets isSpeaking/intensity', () => {
    const { instances, cancel } = installSpeechSynthesis()
    const { result } = renderHook(() => useTextToSpeech())

    act(() => result.current.speak('hello', 'en'))
    act(() => instances[0].onstart?.())
    act(() => instances[0].onboundary?.())

    act(() => result.current.stop())

    expect(cancel).toHaveBeenCalled()
    expect(result.current.isSpeaking).toBe(false)
    expect(result.current.getIntensity()).toBe(0)
  })
})
