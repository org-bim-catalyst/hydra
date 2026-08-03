import { useCallback, useRef, useState } from 'react'
import { synthesizeSpeech } from '../api/voiceApi'
import { useTextToSpeech } from './useTextToSpeech'
import { useVoiceAnalyzer } from './useVoiceAnalyzer'
import { probeRecoveryIfDegraded, useVoiceProviderStatus } from './voiceProviderStatus'

/**
 * FR-006's "speak every AI reply aloud" — tries ElevenLabs first (matching the primary
 * engine used by the full conversational voice mode, `useConversationAudio.ts`) and falls
 * back to the browser's native `useTextToSpeech` the moment ElevenLabs is unavailable,
 * rather than surfacing a bare "Voice output failed" with no recourse. Shares
 * `useVoiceProviderStatus` with the conversational path (voiceProviderStatus.ts) so a
 * failover recorded by one is respected by the other — no repeated, doomed ElevenLabs
 * attempts once a session is known to be degraded.
 *
 * `useTextToSpeech` primes its own voice list at mount time regardless of which engine
 * ultimately speaks (see its own doc comment) — that's the "initialize TTS in the
 * background" the fallback path relies on being ready by the time it's actually needed.
 */
export function useVoiceOutput() {
  const fallback = useTextToSpeech()
  const analyzer = useVoiceAnalyzer()
  const { provider, failOver } = useVoiceProviderStatus()
  const [isSpeaking, setIsSpeaking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const abortControllerRef = useRef<AbortController | null>(null)

  const clearError = useCallback(() => {
    setError(null)
    fallback.clearError()
  }, [fallback])

  const speak = useCallback(
    async (text: string, language: string) => {
      if (!text.trim()) return

      await probeRecoveryIfDegraded(language)
      if (useVoiceProviderStatus.getState().provider === 'fallback') {
        fallback.speak(text, language)
        return
      }

      const controller = new AbortController()
      abortControllerRef.current = controller
      setIsSpeaking(true)
      setError(null)
      let sawAudio = false

      try {
        for await (const event of synthesizeSpeech(text, language, controller.signal)) {
          switch (event.type) {
            case 'audio-chunk':
              sawAudio = true
              analyzer.playAudioChunk(event.audio)
              break
            case 'audio-failed':
              console.error(
                'Voice output: ElevenLabs reported audio-failed mid-stream; failing over.',
              )
              failOver()
              break
            case 'error':
              console.error(
                `Voice output: ElevenLabs stream error — ${event.errorType}: ${event.detail}`,
              )
              setError(event.detail)
              break
            case 'done':
              analyzer.endStream()
              break
            default:
              break
          }
        }
      } catch (err) {
        if (!controller.signal.aborted) {
          // ElevenLabs unreachable at the network level (not just a mid-stream failure the
          // backend already reported as `audio-failed`) — same visible-failover contract.
          console.error('Voice output: /ai/voice/speak request failed.', err)
          failOver()
        }
      } finally {
        setIsSpeaking(false)
        abortControllerRef.current = null
      }

      if (!sawAudio && useVoiceProviderStatus.getState().provider === 'fallback') {
        // Nothing played and we just failed over — the reply still deserves to be heard.
        fallback.speak(text, language)
      }
    },
    [fallback, analyzer, failOver],
  )

  const stop = useCallback(() => {
    abortControllerRef.current?.abort()
    abortControllerRef.current = null
    analyzer.reset()
    setIsSpeaking(false)
    fallback.stop()
  }, [analyzer, fallback])

  return {
    isSupported: true,
    speak,
    stop,
    isSpeaking: isSpeaking || fallback.isSpeaking,
    getIntensity: provider === 'fallback' ? fallback.getIntensity : analyzer.getReactiveIntensity,
    error: error ?? fallback.error,
    clearError,
  }
}
