import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useVoiceProviderStatus } from './voiceProviderStatus'

vi.mock('../api/voiceApi', () => ({
  streamVoiceReply: vi.fn(),
}))

import { streamVoiceReply, type VoiceReplyEvent } from '../api/voiceApi'
import { useSpeechSynthesis } from './useSpeechSynthesis'

async function* toAsyncGenerator(events: VoiceReplyEvent[]): AsyncGenerator<VoiceReplyEvent> {
  for (const event of events) {
    yield event
  }
}

describe('useSpeechSynthesis', () => {
  beforeEach(() => {
    useVoiceProviderStatus.setState({ provider: 'primary', degradedNoticeVisible: false })
    vi.mocked(streamVoiceReply).mockReset()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('routes transcript-delta and audio-chunk events to their callbacks as they arrive (FR-008)', async () => {
    vi.mocked(streamVoiceReply).mockReturnValue(
      toAsyncGenerator([
        { type: 'transcript-delta', content: 'Hello' },
        { type: 'audio-chunk', sequence: 0, audio: 'YWJj' },
        { type: 'transcript-delta', content: ' world' },
        { type: 'audio-chunk', sequence: 1, audio: 'ZGVm' },
        { type: 'done' },
      ]),
    )

    const onTranscriptDelta = vi.fn()
    const onAudioChunk = vi.fn()
    const onDone = vi.fn()
    const { result } = renderHook(() => useSpeechSynthesis())

    await act(async () => {
      await result.current.speak('chat-1', [], 'provider-1', 'model-1', undefined, 'en', {
        onTranscriptDelta,
        onAudioChunk,
        onDone,
      })
    })

    expect(onTranscriptDelta.mock.calls.map((c) => c[0])).toEqual(['Hello', ' world'])
    expect(onAudioChunk.mock.calls.map((c) => c[0])).toEqual(['YWJj', 'ZGVm'])
    expect(onDone).toHaveBeenCalledTimes(1)
    expect(result.current.isSpeaking).toBe(false)
  })

  it('fails over and calls onAudioFailed, without surfacing it as a visible error, on an audio-failed event (FR-033)', async () => {
    vi.mocked(streamVoiceReply).mockReturnValue(
      toAsyncGenerator([
        { type: 'transcript-delta', content: 'Hello' },
        { type: 'audio-failed' },
        { type: 'done' },
      ]),
    )

    const onAudioFailed = vi.fn()
    const { result } = renderHook(() => useSpeechSynthesis())

    await act(async () => {
      await result.current.speak('chat-1', [], 'provider-1', 'model-1', undefined, 'en', {
        onTranscriptDelta: vi.fn(),
        onAudioChunk: vi.fn(),
        onDone: vi.fn(),
        onAudioFailed,
      })
    })

    expect(onAudioFailed).toHaveBeenCalledTimes(1)
    expect(useVoiceProviderStatus.getState().provider).toBe('fallback')
    expect(result.current.error).toBeNull()
  })

  it('does not surface an error when the stream is aborted intentionally (interruption/stop)', async () => {
    let rejectStream: (reason: unknown) => void = () => {}
    vi.mocked(streamVoiceReply).mockImplementation(async function* () {
      await new Promise((_resolve, reject) => {
        rejectStream = reject
      })
      yield { type: 'done' } as VoiceReplyEvent
    })

    const { result } = renderHook(() => useSpeechSynthesis())

    await act(async () => {
      const speakPromise = result.current.speak(
        'chat-1',
        [],
        'provider-1',
        'model-1',
        undefined,
        'en',
        {
          onTranscriptDelta: vi.fn(),
          onAudioChunk: vi.fn(),
          onDone: vi.fn(),
        },
      )
      // Simulates fetch's own AbortError once the request is actually cancelled.
      result.current.abort()
      rejectStream(new DOMException('Aborted', 'AbortError'))
      await speakPromise
    })

    expect(result.current.error).toBeNull()
    expect(result.current.isSpeaking).toBe(false)
  })
})
