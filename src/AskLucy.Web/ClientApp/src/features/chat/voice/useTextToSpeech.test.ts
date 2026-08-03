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

const DEFAULT_VOICES = [
  {
    name: 'Microsoft David Desktop',
    lang: 'en-US',
    localService: true,
    default: false,
    voiceURI: 'david',
  },
  {
    name: 'Microsoft Zira Desktop',
    lang: 'en-US',
    localService: true,
    default: false,
    voiceURI: 'zira',
  },
] as SpeechSynthesisVoice[]

/** Defaults to a voice list containing the curated en/chromium voice (`selectPersonaVoice`,
 * spec 010-lucy-brand-refresh) so existing lifecycle tests still get past the voice-selection
 * step unchanged; pass `voices: []` explicitly to exercise the no-voice-available error path. */
function installSpeechSynthesis(voices: SpeechSynthesisVoice[] = DEFAULT_VOICES) {
  const instances: FakeUtteranceInstance[] = []
  let currentVoices = voices

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
  const addEventListener = vi.fn()
  const removeEventListener = vi.fn()
  vi.stubGlobal('SpeechSynthesisUtterance', FakeUtterance)
  vi.stubGlobal('speechSynthesis', {
    speak,
    cancel,
    getVoices: () => currentVoices,
    addEventListener,
    removeEventListener,
  })
  // detectBrowserEngine reads navigator.userAgentData/userAgent — force 'chromium' so the
  // curated lookup in DEFAULT_VOICES actually resolves deterministically in tests.
  vi.stubGlobal('navigator', { userAgent: 'Chrome/120.0.0.0', userAgentData: undefined })

  return {
    instances,
    speak,
    cancel,
    addEventListener,
    removeEventListener,
    setVoices: (next: SpeechSynthesisVoice[]) => {
      currentVoices = next
    },
  }
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

  it('applies the curated persona voice to the utterance (FR-001/FR-003, contracts/voice-persona-mapping.md)', () => {
    const { instances, speak: speakSpy } = installSpeechSynthesis()
    const { result } = renderHook(() => useTextToSpeech())

    act(() => result.current.speak('hello', 'en'))

    expect(instances[0].voice).toEqual(expect.objectContaining({ name: 'Microsoft Zira Desktop' }))
    expect(speakSpy).toHaveBeenCalledTimes(1)
  })

  it('surfaces a visible error and never calls speechSynthesis.speak() when no voice matches the language (FR-005)', () => {
    const { instances, speak: speakSpy } = installSpeechSynthesis([])
    const { result } = renderHook(() => useTextToSpeech())

    act(() => result.current.speak('hello', 'en'))

    expect(instances).toHaveLength(0)
    expect(speakSpy).not.toHaveBeenCalled()
    expect(result.current.error).toBe('Voice output failed. Please try again.')
  })

  it('recovers once voiceschanged delivers a populated list, even though getVoices() was empty at mount (Chromium async-enumeration race)', () => {
    const { instances, speak: speakSpy, addEventListener, setVoices } = installSpeechSynthesis([])
    const { result } = renderHook(() => useTextToSpeech())

    // Simulate the browser finishing its async voice enumeration after mount.
    setVoices(DEFAULT_VOICES)
    const voiceschangedHandler = addEventListener.mock.calls.find(
      ([event]) => event === 'voiceschanged',
    )?.[1]
    act(() => voiceschangedHandler?.())

    act(() => result.current.speak('hello', 'en'))

    expect(instances[0].voice).toEqual(expect.objectContaining({ name: 'Microsoft Zira Desktop' }))
    expect(speakSpy).toHaveBeenCalledTimes(1)
    expect(result.current.error).toBeNull()
  })

  it('recovers via polling when voiceschanged never fires (Chromium/Edge do not always fire it reliably)', () => {
    vi.useFakeTimers()
    try {
      const { instances, speak: speakSpy, setVoices } = installSpeechSynthesis([])
      const { result } = renderHook(() => useTextToSpeech())

      // The browser's async enumeration finishes, but only discoverable via polling here —
      // 'voiceschanged' is never fired, unlike the sibling test above.
      setVoices(DEFAULT_VOICES)
      act(() => {
        vi.advanceTimersByTime(200)
      })

      act(() => result.current.speak('hello', 'en'))

      expect(instances[0].voice).toEqual(
        expect.objectContaining({ name: 'Microsoft Zira Desktop' }),
      )
      expect(speakSpy).toHaveBeenCalledTimes(1)
      expect(result.current.error).toBeNull()
    } finally {
      vi.useRealTimers()
    }
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
