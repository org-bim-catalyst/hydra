import { useCallback, useRef, useState } from 'react'
import type { ChatMessage, GenerationParameters } from '../api/aiApi'
import { streamVoiceReply } from '../api/voiceApi'
import { useVoiceProviderStatus } from './voiceProviderStatus'

interface SpeakCallbacks {
  onTranscriptDelta: (text: string) => void
  /** Routes into the shared `useVoiceAnalyzer` instance (research.md Decision 6) — this hook
   * does not own an analyzer itself, since exactly one must exist per voice session. */
  onAudioChunk: (base64Audio: string) => void
  onDone: () => void
  /** FR-033: a TTS-specific failure mid-stream — the text reply still completes normally. */
  onAudioFailed?: () => void
}

/**
 * Primary-path text-to-speech: calls `voiceApi.streamVoiceReply()` and dispatches each
 * multiplexed event to the caller (`useConversationAudio.ts`), which owns the shared
 * transcript state and `useVoiceAnalyzer` instance (FR-008).
 */
export function useSpeechSynthesis() {
  const [isSpeaking, setIsSpeaking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const abortControllerRef = useRef<AbortController | null>(null)
  const { failOver } = useVoiceProviderStatus()

  const speak = useCallback(
    async (
      chatId: string,
      messages: ChatMessage[],
      providerId: string,
      modelId: string,
      generationParameters: GenerationParameters | undefined,
      language: string,
      callbacks: SpeakCallbacks,
    ) => {
      const controller = new AbortController()
      abortControllerRef.current = controller
      setIsSpeaking(true)
      setError(null)

      try {
        for await (const event of streamVoiceReply(
          chatId,
          messages,
          providerId,
          modelId,
          generationParameters,
          language,
          controller.signal,
        )) {
          switch (event.type) {
            case 'transcript-delta':
              callbacks.onTranscriptDelta(event.content)
              break
            case 'audio-chunk':
              callbacks.onAudioChunk(event.audio)
              break
            case 'audio-failed':
              failOver()
              callbacks.onAudioFailed?.()
              break
            case 'error':
              setError(event.detail)
              break
            case 'done':
              callbacks.onDone()
              break
            case 'provider-status':
            case 'usage':
              break
          }
        }
      } catch (err) {
        // An aborted fetch (interruption/stop, FR-017/FR-023) is expected and not an error
        // the user needs to see — constitution §2.VIII's "no silent failures" applies to
        // unexpected failures, not a cancellation the user themselves triggered.
        if (!controller.signal.aborted) {
          setError(err instanceof Error ? err.message : 'The voice reply failed. Please try again.')
        }
      } finally {
        setIsSpeaking(false)
        abortControllerRef.current = null
      }
    },
    [failOver],
  )

  /** FR-017/FR-023: cancels both LLM generation and TTS synthesis together, since both ride
   * the same `/api/v1/ai/voice/reply` request (contracts/voice-reply-stream.md). */
  const abort = useCallback(() => {
    abortControllerRef.current?.abort()
    abortControllerRef.current = null
    setIsSpeaking(false)
  }, [])

  const clearError = useCallback(() => setError(null), [])

  return { isSpeaking, error, speak, abort, clearError }
}
