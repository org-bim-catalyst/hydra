import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { VoiceReplyEvent } from '../api/voiceApi'
import { useVoiceProviderStatus } from './voiceProviderStatus'
import { useVoiceOutput } from './useVoiceOutput'

const synthesizeSpeechMock = vi.fn()
vi.mock('../api/voiceApi', () => ({
  synthesizeSpeech: (...args: unknown[]) => synthesizeSpeechMock(...args),
}))

const fallbackStub = {
  isSupported: true,
  speak: vi.fn(),
  stop: vi.fn(),
  isSpeaking: false,
  getIntensity: vi.fn(() => 0),
  error: null as string | null,
  clearError: vi.fn(),
}
vi.mock('./useTextToSpeech', () => ({
  useTextToSpeech: () => fallbackStub,
}))

const analyzerStub = {
  playAudioChunk: vi.fn(),
  endStream: vi.fn(),
  getReactiveIntensity: vi.fn(() => 0),
  setMuted: vi.fn(),
  reset: vi.fn(),
}
vi.mock('./useVoiceAnalyzer', () => ({
  useVoiceAnalyzer: () => analyzerStub,
}))

/** A stream that yields the given events, then never resolves further — simulates a reply
 * still actively streaming/speaking (as opposed to one that has reached `done`). */
function neverEndingStream(events: VoiceReplyEvent[]): AsyncGenerator<VoiceReplyEvent> {
  async function* gen() {
    for (const event of events) yield event
    await new Promise<never>(() => {})
  }
  return gen()
}

describe('useVoiceOutput mute (US1, FR-002/FR-003, research.md Decision 3)', () => {
  beforeEach(() => {
    useVoiceProviderStatus.setState({ provider: 'primary', degradedNoticeVisible: false })
    synthesizeSpeechMock.mockReset()
    analyzerStub.reset.mockClear()
    analyzerStub.playAudioChunk.mockClear()
    fallbackStub.stop.mockClear()
    fallbackStub.speak.mockClear()
  })

  afterEach(() => {
    vi.clearAllTimers()
  })

  it('speak() is a no-op while isMuted is true — no network call, isSpeaking stays false', async () => {
    const { result } = renderHook(() => useVoiceOutput())

    act(() => result.current.setMuted(true))
    expect(result.current.isMuted).toBe(true)

    await act(async () => {
      await result.current.speak('Hello there', 'en')
    })

    expect(synthesizeSpeechMock).not.toHaveBeenCalled()
    expect(result.current.isSpeaking).toBe(false)
  })

  it('setMuted(true) while isSpeaking is true stops playback immediately', async () => {
    synthesizeSpeechMock.mockReturnValue(
      neverEndingStream([{ type: 'audio-chunk', sequence: 0, audio: 'AAAA' }]),
    )
    const { result } = renderHook(() => useVoiceOutput())

    act(() => {
      void result.current.speak('Hello there', 'en')
    })

    await waitFor(() => expect(result.current.isSpeaking).toBe(true))

    act(() => result.current.setMuted(true))

    expect(result.current.isSpeaking).toBe(false)
    expect(analyzerStub.reset).toHaveBeenCalled()
    expect(fallbackStub.stop).toHaveBeenCalled()
  })

  it('unmuting after a reply completed while muted does not retroactively start playback', async () => {
    const { result } = renderHook(() => useVoiceOutput())

    act(() => result.current.setMuted(true))
    await act(async () => {
      // Simulates the completed-while-muted reply: the auto-speak call site would invoke
      // speak() here in ChatPage.tsx, but muted speak() is a no-op (previous test) — nothing
      // is queued for later.
      await result.current.speak('A reply generated while muted', 'en')
    })

    act(() => result.current.setMuted(false))

    expect(result.current.isMuted).toBe(false)
    expect(result.current.isSpeaking).toBe(false)
    expect(synthesizeSpeechMock).not.toHaveBeenCalled()
  })

  it('toggleMute flips isMuted', () => {
    const { result } = renderHook(() => useVoiceOutput())
    expect(result.current.isMuted).toBe(false)

    act(() => result.current.toggleMute())
    expect(result.current.isMuted).toBe(true)

    act(() => result.current.toggleMute())
    expect(result.current.isMuted).toBe(false)
  })
})
