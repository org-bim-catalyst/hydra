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
 * complete → mic returns to idle (US1), auto-resumes in Continuous mode (US2).
 *
 * specs/033-hold-to-talk-and-echo-fix FR-009 (resolved clarification): the microphone's input
 * is fully muted for the duration of `AiSpeaking` (via `recognition.setInputMuted`) so Lucy's
 * own voice output can never be picked up as user speech, regardless of acoustic echo
 * cancellation quality. This deliberately supersedes the prior "natural interruption" design
 * (specs/031 research.md Decision 10, US3) — mid-response barge-in is no longer possible, and
 * the local-speech-pre-trigger/ducking machinery that existed only to support it has been
 * removed as dead code, not merely disabled.
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

  const fallbackAlsoFailedRef = useRef(false)

  /** Holds `recognition.start`/`recognition.setInputMuted` (assigned via effects once
   * `recognition` exists further below) so `runAssistantTurn` — defined before `recognition`
   * for readability (turn logic first, the hook that drives it second) — can trigger
   * Continuous mode's auto-relisten (FR-014) and mute the mic during `AiSpeaking` (FR-009)
   * without a circular lexical dependency on `recognition` itself. */
  const recognitionStartRef = useRef<() => Promise<void>>(async () => {})
  const recognitionSetInputMutedRef = useRef<(muted: boolean) => void>(() => {})

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
          if (voiceState.state !== 'AiSpeaking') {
            voiceState.setState('AiSpeaking')
            recognitionSetInputMutedRef.current(true)
          }
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
      recognitionSetInputMutedRef.current(false) // Lucy's done speaking — resume normal listening.

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

  const handleFinalTranscript = useCallback(
    async (text: string) => {
      voiceState.setState('UserSpeaking')
      await runAssistantTurn(text)
    },
    [voiceState, runAssistantTurn],
  )

  const recognition = useSpeechRecognition({
    language,
    mode,
    onPartialTranscript: () => {
      if (voiceState.state === 'Listening') voiceState.setState('UserSpeaking')
    },
    onFinalTranscript: handleFinalTranscript,
    preferredMicrophoneDeviceId,
  })

  useEffect(() => {
    recognitionStartRef.current = recognition.start
    recognitionSetInputMutedRef.current = recognition.setInputMuted
  }, [recognition.start, recognition.setInputMuted])

  const startTurn = useCallback(async () => {
    fallbackAlsoFailedRef.current = false
    // specs/034-transcription-crash-gesture-and-continuous-view: `recognition.start()`'s own
    // getUserMedia-denial path already resolves normally (setting permissionState instead of
    // throwing), handled below — but anything else it can throw (e.g. a missing browser API)
    // was previously uncaught here, becoming an unhandled rejection for any caller that doesn't
    // itself await+catch startTurn() (constitution §2.VIII). Caught here instead, once, so every
    // caller gets a visible error through the existing errorMessage/voiceState mechanism.
    try {
      await probeRecoveryIfDegraded(language)
      voiceState.setState('Listening')
      await recognition.start()

      if (recognition.permissionState === 'denied' && provider === 'fallback') {
        handleUnrecoverableFailure(
          'Voice input is unavailable: the microphone was denied and the backup voice engine also could not start.',
        )
      }
    } catch (err) {
      handleUnrecoverableFailure(
        err instanceof Error ? err.message : 'Voice input could not start. Please try again.',
      )
    }
  }, [language, voiceState, recognition, provider, handleUnrecoverableFailure])

  /** FR-023: stop cancels playback+generation, clears the queue, resets the sphere, and
   * resumes listening automatically if Continuous mode is active — the same abort path as
   * a user-triggered stop mid-response. Also unmutes the mic (specs/033 FR-009), in case stop()
   * is called while `AiSpeaking` had it muted. */
  const stop = useCallback(async () => {
    synthesis.abort()
    analyzer.reset()
    recognition.setInputMuted(false)

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
    getMicIntensity: recognition.getMicIntensity,
    // Mute *state* is owned by voicePreferencesStore (persisted, FR-029); this is only the
    // audio-graph effect of that state — callers apply it via an effect, not a local toggle.
    setMuted: analyzer.setMuted,
    startTurn,
    stop,
    cancelListening,
    clearError: voiceState.reset,
  }
}
