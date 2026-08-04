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
  const { provider, failOver } = useVoiceProviderStatus()
  const [isSpeaking, setIsSpeaking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [isMuted, setIsMutedState] = useState(false)
  const abortControllerRef = useRef<AbortController | null>(null)

  // Surfaces failures from the ElevenLabs audio element itself (blocked autoplay, a
  // mid-stream decode error) — constitution §2.VIII: these must reach the user the same
  // way a failed ElevenLabs HTTP request does, not just this hook's own console.error.
  const handlePlaybackError = useCallback(
    (message: string) => {
      console.error(`Voice output: ${message}`)
      failOver()
      setError('Voice output failed. Please try again.')
    },
    [failOver],
  )

  const analyzer = useVoiceAnalyzer(handlePlaybackError)

  const clearError = useCallback(() => {
    setError(null)
    fallback.clearError()
  }, [fallback])

  const speak = useCallback(
    async (text: string, language: string) => {
      if (!text.trim()) return
      // FR-003/Clarification Q2: a reply is never queued or started while muted, so there is
      // nothing left to become audible later — unmuting only affects the *next* speak() call.
      if (isMuted) return

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
    [fallback, analyzer, failOver, isMuted],
  )

  const stop = useCallback(() => {
    abortControllerRef.current?.abort()
    abortControllerRef.current = null
    analyzer.reset()
    setIsSpeaking(false)
    fallback.stop()
  }, [analyzer, fallback])

  const combinedIsSpeaking = isSpeaking || fallback.isSpeaking

  // US1/FR-002/FR-003 (research.md Decision 3): muting stops whatever is currently audible
  // immediately (rather than just silently continuing in the background, which would risk
  // becoming audible again on unmute) but never interrupts or delays reply generation —
  // by the time this hook's speak() runs, the AI's text reply has already fully generated.
  const setMuted = useCallback(
    (muted: boolean) => {
      setIsMutedState(muted)
      if (muted && combinedIsSpeaking) {
        stop()
      }
    },
    [combinedIsSpeaking, stop],
  )

  const toggleMute = useCallback(() => setMuted(!isMuted), [setMuted, isMuted])

  return {
    isSupported: true,
    speak,
    stop,
    isSpeaking: combinedIsSpeaking,
    getIntensity: provider === 'fallback' ? fallback.getIntensity : analyzer.getReactiveIntensity,
    error: error ?? fallback.error,
    clearError,
    isMuted,
    setMuted,
    toggleMute,
  }
}
