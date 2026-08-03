import { useCallback, useEffect, useRef } from 'react'
import type { ChatMessage, GenerationParameters } from '../api/aiApi'
import type { ConversationMode } from './useSpeechRecognition'
import { useSpeechRecognition } from './useSpeechRecognition'
import { useSpeechSynthesis } from './useSpeechSynthesis'
import { useVoiceAnalyzer } from './useVoiceAnalyzer'
import { useVoiceState } from './useVoiceState'
import { probeRecoveryIfDegraded, useVoiceProviderStatus } from './voiceProviderStatus'

interface UseConversationAudioOptions {
  chatId: string
  language: string
  mode: ConversationMode
  providerId: string
  modelId: string
  generationParameters?: GenerationParameters
  /** FR-031: passed straight through to `useSpeechRecognition`'s own device-availability
   * check. */
  preferredMicrophoneDeviceId?: string | null
  /** Returns the full message history plus the just-finalized user transcript — the caller
   * (e.g. `ChatPage.tsx`) owns conversation state; this hook only orchestrates audio. */
  buildMessages: (userTranscript: string) => ChatMessage[]
  onUserTranscript: (text: string) => void
  onAssistantTextDelta: (text: string) => void
  onAssistantTurnComplete: () => void
}

/**
 * Conversation Coordinator for one voice session (spec.md Key Entity "Voice Session"):
 * mic → `useSpeechRecognition` → finalized transcript → `useSpeechSynthesis` → playback
 * complete → mic returns to idle (US1), auto-resumes in Continuous mode (US2), and reacts to
 * a local speech pre-trigger during `AiSpeaking` for natural interruption (US3,
 * research.md Decision 10).
 *
 * Recognition is kept connected through the `AiSpeaking` phase (not just during `Listening`)
 * so the same live audio feed that streams to ElevenLabs also drives the local
 * amplitude-threshold interruption pre-trigger — a second, separate "passive listener" would
 * duplicate the mic-capture graph for no benefit.
 */
export function useConversationAudio({
  chatId,
  language,
  mode,
  providerId,
  modelId,
  generationParameters,
  preferredMicrophoneDeviceId,
  buildMessages,
  onUserTranscript,
  onAssistantTextDelta,
  onAssistantTurnComplete,
}: UseConversationAudioOptions) {
  const voiceState = useVoiceState()
  const analyzer = useVoiceAnalyzer()
  const synthesis = useSpeechSynthesis()
  const { provider, degradedNoticeVisible } = useVoiceProviderStatus()

  /** research.md Decision 10: set the instant a local pre-trigger fires during `AiSpeaking`;
   * cleared once either a real interruption is confirmed or the duck window times out. */
  const duckTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const isDuckedRef = useRef(false)
  const fallbackAlsoFailedRef = useRef(false)

  /** Holds `recognition.start` (assigned via effect once `recognition` exists further below)
   * so `runAssistantTurn` — defined before `recognition` for readability (turn logic first,
   * the hook that drives it second) — can trigger Continuous mode's auto-relisten (FR-014)
   * without a circular lexical dependency on `recognition` itself. */
  const recognitionStartRef = useRef<() => Promise<void>>(async () => {})

  const clearDuckTimeout = () => {
    if (duckTimeoutRef.current) {
      clearTimeout(duckTimeoutRef.current)
      duckTimeoutRef.current = null
    }
  }

  /** FR-032/FR-036: both the primary path (recognition/synthesis failure → fallback) and the
   * fallback path itself failing (e.g., mic permission denied) surface a visible, actionable
   * error — never an indefinite listening/processing state. */
  const handleUnrecoverableFailure = useCallback(
    (message: string) => {
      fallbackAlsoFailedRef.current = true
      voiceState.setError(message)
    },
    [voiceState],
  )

  const runAssistantTurn = useCallback(
    async (userTranscript: string) => {
      voiceState.setState('Processing')
      onUserTranscript(userTranscript)

      voiceState.setState('AiThinking')
      const messages = buildMessages(userTranscript)

      await synthesis.speak(chatId, messages, providerId, modelId, generationParameters, language, {
        onTranscriptDelta: onAssistantTextDelta,
        onAudioChunk: (audio) => {
          if (voiceState.state !== 'AiSpeaking') voiceState.setState('AiSpeaking')
          analyzer.playAudioChunk(audio)
        },
        onAudioFailed: () => {
          // FR-033: audio for the rest of this turn falls back client-side; the text
          // stream (already routed through onTranscriptDelta) continues unaffected.
        },
        onDone: () => {
          analyzer.endStream()
        },
      })

      onAssistantTurnComplete()
      analyzer.reset()

      if (mode === 'continuous') {
        voiceState.setState('Listening')
        await probeRecoveryIfDegraded(language)
        await recognitionStartRef.current()
      } else {
        voiceState.setState('Idle')
      }
    },
    [
      voiceState,
      onUserTranscript,
      buildMessages,
      synthesis,
      chatId,
      providerId,
      modelId,
      generationParameters,
      language,
      onAssistantTextDelta,
      onAssistantTurnComplete,
      analyzer,
      mode,
    ],
  )

  const handleLocalSpeechLikely = useCallback(() => {
    if (voiceState.state !== 'AiSpeaking' || isDuckedRef.current) return

    // Fast, reversible, purely local reaction (research.md Decision 10) — ducks immediately,
    // well ahead of the round trip needed to confirm real speech.
    isDuckedRef.current = true
    analyzer.setMuted(true)
    voiceState.setState('Interrupted')

    clearDuckTimeout()
    duckTimeoutRef.current = setTimeout(() => {
      // No confirming transcript arrived — false positive (e.g. a cough); resume.
      if (isDuckedRef.current) {
        isDuckedRef.current = false
        analyzer.setMuted(false)
        voiceState.setState('AiSpeaking')
      }
    }, 1500)
  }, [voiceState, analyzer])

  const handleFinalTranscript = useCallback(
    async (text: string) => {
      const wasInterruption = isDuckedRef.current
      if (wasInterruption) {
        clearDuckTimeout()
        isDuckedRef.current = false
        synthesis.abort()
        analyzer.reset()
      }

      voiceState.setState('UserSpeaking')
      await runAssistantTurn(text)
    },
    [synthesis, analyzer, voiceState, runAssistantTurn],
  )

  const recognition = useSpeechRecognition({
    language,
    mode,
    onPartialTranscript: () => {
      if (voiceState.state === 'Listening') voiceState.setState('UserSpeaking')
    },
    onFinalTranscript: handleFinalTranscript,
    onLocalSpeechLikely: handleLocalSpeechLikely,
    preferredMicrophoneDeviceId,
  })

  useEffect(() => {
    recognitionStartRef.current = recognition.start
  }, [recognition.start])

  const startTurn = useCallback(async () => {
    fallbackAlsoFailedRef.current = false
    await probeRecoveryIfDegraded(language)
    voiceState.setState('Listening')
    await recognition.start()

    if (recognition.permissionState === 'denied' && provider === 'fallback') {
      handleUnrecoverableFailure(
        'Voice input is unavailable: the microphone was denied and the backup voice engine also could not start.',
      )
    }
  }, [language, voiceState, recognition, provider, handleUnrecoverableFailure])

  /** FR-023: stop cancels playback+generation, clears the queue, resets the sphere, and
   * resumes listening automatically if Continuous mode is active — the same abort path as
   * interruption (US3), triggered by the user instead of a detected utterance. */
  const stop = useCallback(async () => {
    clearDuckTimeout()
    isDuckedRef.current = false
    synthesis.abort()
    analyzer.reset()

    if (mode === 'continuous') {
      voiceState.setState('Listening')
      await probeRecoveryIfDegraded(language)
      await recognition.start()
    } else {
      voiceState.reset()
    }
  }, [synthesis, analyzer, mode, voiceState, language, recognition])

  const cancelListening = useCallback(() => {
    recognition.cancel()
    voiceState.reset()
  }, [recognition, voiceState])

  return {
    voiceState: voiceState.state,
    errorMessage: voiceState.errorMessage,
    provider,
    degradedNoticeVisible,
    deviceNotice: recognition.deviceNotice,
    clearDeviceNotice: recognition.clearDeviceNotice,
    getReactiveIntensity: analyzer.getReactiveIntensity,
    // Mute *state* is owned by voicePreferencesStore (persisted, FR-029); this is only the
    // audio-graph effect of that state — callers apply it via an effect, not a local toggle.
    setMuted: analyzer.setMuted,
    startTurn,
    stop,
    cancelListening,
    clearError: voiceState.reset,
  }
}
