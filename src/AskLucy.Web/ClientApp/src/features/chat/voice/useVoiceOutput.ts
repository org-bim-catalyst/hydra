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
  // The text being heard right now — set when its audio actually starts, cleared when it ends —
  // so the chat can highlight the bubble Lucy is reading. Text, not a message id: ids change
  // while a turn streams (the first message is re-keyed when its server id arrives).
  const [speakingText, setSpeakingText] = useState<string | null>(null)
  const abortControllerRef = useRef<AbortController | null>(null)
  // Replies are voiced one at a time. ChatPage speaks each reply the moment it completes, and a
  // turn can complete several within a few seconds, so without a queue the second started over
  // the first (reported 2026-10-01). `generation` lets stop() and mute drop whatever is waiting.
  const queueRef = useRef<Promise<void>>(Promise.resolve())
  const generationRef = useRef(0)
  const pendingRef = useRef(0)
  // Every queued reply's `onAudible`, so stop() can release the ones still waiting without
  // depending on the reply in front of them winding down first.
  const announcersRef = useRef(new Set<() => void>())

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

  const speakNow = useCallback(
    async (text: string, language: string, isCurrent: () => boolean, announce: () => void) => {
      await probeRecoveryIfDegraded(language)
      if (!isCurrent()) return
      if (useVoiceProviderStatus.getState().provider === 'fallback') {
        setSpeakingText(text)
        announce()
        await fallback.speak(text, language)
        return
      }

      const controller = new AbortController()
      abortControllerRef.current = controller
      setError(null)
      let sawAudio = false

      try {
        for await (const event of synthesizeSpeech(text, language, controller.signal)) {
          switch (event.type) {
            case 'audio-chunk':
              if (!sawAudio && isCurrent()) {
                setSpeakingText(text)
                announce()
              }
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
        abortControllerRef.current = null
      }

      if (!isCurrent()) return
      if (sawAudio) {
        // The stream has finished ARRIVING; the audio is still playing. Hold the queue until it
        // has been heard. Sealing first covers a stream that ended without `done`.
        analyzer.endStream()
        await analyzer.waitForPlaybackToEnd()
      } else if (useVoiceProviderStatus.getState().provider === 'fallback') {
        // Nothing played and we just failed over — the reply still deserves to be heard.
        if (isCurrent()) setSpeakingText(text)
        announce()
        await fallback.speak(text, language)
      }
    },
    [fallback, analyzer, failOver],
  )

  /**
   * Queues `text` to be spoken after whatever is already being said.
   *
   * `onAudible` is how a caller that is holding something back until it can be heard (the chat
   * keeps a reply hidden behind a thinking indicator while its voice is made) learns it can let
   * go. It runs exactly once: when the audio starts, or - on every path where it never will, a
   * muted or empty request, an engine that failed with nothing to fall back on, a stop - as soon
   * as that is known. Never leaving it uncalled is what keeps a failed voice from hiding text.
   */
  const speak = useCallback(
    (text: string, language: string, onAudible?: () => void): Promise<void> => {
      let announced = false
      const announce = () => {
        if (announced) return
        announced = true
        onAudible?.()
      }
      if (!text.trim()) {
        announce()
        return Promise.resolve()
      }
      // FR-003/Clarification Q2: a reply is never queued or started while muted, so there is
      // nothing left to become audible later — unmuting only affects the *next* speak() call.
      if (isMuted) {
        announce()
        return Promise.resolve()
      }

      const generation = generationRef.current
      const isCurrent = () => generation === generationRef.current
      pendingRef.current += 1
      announcersRef.current.add(announce)
      setIsSpeaking(true)

      const run = queueRef.current
        .then(() => (isCurrent() ? speakNow(text, language, isCurrent, announce) : undefined))
        .finally(() => {
          announce()
          announcersRef.current.delete(announce)
          // A stop() since this was queued already reset the count and the speaking state.
          if (!isCurrent()) return
          pendingRef.current -= 1
          setIsSpeaking(pendingRef.current > 0)
          setSpeakingText(null)
        })
      // The caller owns this promise's rejection; the queue itself must keep moving regardless.
      queueRef.current = run.then(
        () => undefined,
        () => undefined,
      )
      return run
    },
    [isMuted, speakNow],
  )

  const stop = useCallback(() => {
    generationRef.current += 1
    pendingRef.current = 0
    for (const announce of announcersRef.current) announce()
    announcersRef.current.clear()
    queueRef.current = Promise.resolve()
    abortControllerRef.current?.abort()
    abortControllerRef.current = null
    analyzer.reset()
    setIsSpeaking(false)
    setSpeakingText(null)
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
    speakingText,
    getIntensity: provider === 'fallback' ? fallback.getIntensity : analyzer.getReactiveIntensity,
    getFrequencyBands:
      provider === 'fallback' ? fallback.getFrequencyBands : analyzer.getFrequencyBands,
    error: error ?? fallback.error,
    clearError,
    isMuted,
    setMuted,
    toggleMute,
  }
}
