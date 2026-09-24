import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../../../api/httpClient'
import { createSttSession } from '../api/voiceApi'
import {
  getBrowserSpeechRecognition,
  isWhisperDictationSupported,
  startBrowserDictation,
  startWhisperDictation,
  type DictationFailure,
  type DictationSession,
  type FallbackEngine,
} from './dictationFallback'
import { downsampleTo16kHz, float32ToInt16Pcm, toBase64 } from './pcm16'
import { useVoiceProviderStatus } from './voiceProviderStatus'

const MAX_RECONNECT_ATTEMPTS = 2 // research.md Decision 8
const RECONNECT_DELAY_MS = 1000
const SILENCE_COMMIT_DELAY_MS = 800 // FR-002: pause before auto-processing
const SAMPLE_RATE_HZ = 16000 // matches downsampleTo16kHz below and the `pcm_16000` audio_format

export type ConversationMode = 'push-to-talk' | 'continuous'
export type MicrophonePermissionState = 'unknown' | 'granted' | 'denied'

interface UseSpeechRecognitionOptions {
  language: string
  mode: ConversationMode
  onPartialTranscript: (text: string) => void
  onFinalTranscript: (text: string) => void
  /** FR-031: a previously saved microphone device id, if any. Checked against
   * `navigator.mediaDevices.enumerateDevices()` at session start — falls back to the
   * platform default (and surfaces {@link deviceNotice}) rather than failing outright when
   * the device is no longer present (e.g. unplugged). */
  preferredMicrophoneDeviceId?: string | null
  /** Every failure the hook surfaces through `error`, as it happens — so a caller that owns
   * the visible voice state can show it rather than leaving the mic "listening". */
  onError?: (message: string) => void
}

const FALLBACK_NOTICES: Record<FallbackEngine, string> = {
  whisper: 'ElevenLabs live dictation is unavailable — using Whisper transcription instead.',
  browser: "ElevenLabs live dictation is unavailable — using your browser's speech recognition instead.",
}

/**
 * Primary-path speech-to-text: mints a session token via `voiceApi.createSttSession`, opens a
 * WebSocket **directly to ElevenLabs** (research.md Decision 2 — the backend never sees the
 * raw audio for this path), and streams 16kHz PCM chunks captured via an `AudioWorkletNode`.
 *
 * Wire protocol verified 2026-08-03 against ElevenLabs' realtime STT documentation
 * (research.md Decision 8, SPEC-013 T010 — this closed the "residual verification item"
 * spec 012's research.md had flagged and left unconfirmed):
 * https://elevenlabs.io/docs/api-reference/speech-to-text/v-1-speech-to-text-realtime and
 * https://elevenlabs.io/docs/eleven-api/guides/how-to/speech-to-text/realtime/event-reference
 *
 * Corrections made from the original (unverified) implementation:
 * - The message discriminator field is `message_type`, not `type`, on both the client→server
 *   and server→client sides.
 * - There is no standalone "commit" message. The client always sends `input_audio_chunk`
 *   messages; setting `commit: true` on one finalizes the current utterance. An empty-audio
 *   `input_audio_chunk` with `commit: true` is sent when committing without new audio to hand
 *   (e.g. the silence-timeout auto-commit).
 * - The audio bytes field is `audio_base_64`, not `audio`, and each chunk must also carry
 *   `sample_rate` (matches `downsampleTo16kHz` below).
 * - The connection URL takes `model_id` and `audio_format` query params alongside `token`.
 * - `partial_transcript`/`committed_transcript` (server→client) were already correct; only
 *   their envelope's discriminator field name (`message_type`) needed fixing.
 *
 * When the realtime session can't be opened — ElevenLabs switched off under Admin → AI
 * providers, unconfigured, or unreachable — the turn continues on a fallback engine
 * (`dictationFallback.ts`: Whisper, else the browser's own recognizer) instead of failing, and
 * `engineNotice` says which one is listening. The provider status store remembers the
 * failover, so later turns go straight to the fallback until `probeRecoveryIfDegraded` finds
 * ElevenLabs healthy again.
 */
export function useSpeechRecognition({
  language,
  mode,
  onPartialTranscript,
  onFinalTranscript,
  preferredMicrophoneDeviceId,
  onError,
}: UseSpeechRecognitionOptions) {
  const [isListening, setIsListening] = useState(false)
  const [permissionState, setPermissionState] = useState<MicrophonePermissionState>('unknown')
  const [error, setError] = useState<string | null>(null)
  const [deviceNotice, setDeviceNotice] = useState<string | null>(null)
  const [engineNotice, setEngineNotice] = useState<string | null>(null)

  const socketRef = useRef<WebSocket | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const workletNodeRef = useRef<AudioWorkletNode | null>(null)
  const sourceRef = useRef<MediaStreamAudioSourceNode | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const silenceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const micAnalyserRef = useRef<AnalyserNode | null>(null)
  const micFrequencyDataRef = useRef<Uint8Array<ArrayBuffer> | null>(null)
  const fallbackSessionRef = useRef<DictationSession | null>(null)
  /** Fallback engines found unusable this page session (e.g. transcription not configured). */
  const unusableEnginesRef = useRef(new Set<FallbackEngine>())
  const modeRef = useRef(mode)
  const onErrorRef = useRef(onError)
  useEffect(() => {
    modeRef.current = mode
    onErrorRef.current = onError
  }, [mode, onError])

  const fail = useCallback((message: string) => {
    setError(message)
    onErrorRef.current?.(message)
  }, [])

  const { failOver } = useVoiceProviderStatus()

  const canStreamRealtime = typeof AudioWorkletNode !== 'undefined' && typeof WebSocket !== 'undefined'
  const isSupported =
    typeof navigator !== 'undefined' &&
    !!navigator.mediaDevices?.getUserMedia &&
    typeof AudioContext !== 'undefined' &&
    (canStreamRealtime || isWhisperDictationSupported() || !!getBrowserSpeechRecognition())

  const clearSilenceTimer = () => {
    if (silenceTimerRef.current) {
      clearTimeout(silenceTimerRef.current)
      silenceTimerRef.current = null
    }
  }

  const cleanupAudioGraph = useCallback(() => {
    clearSilenceTimer()
    fallbackSessionRef.current?.cancel()
    fallbackSessionRef.current = null
    workletNodeRef.current?.port.close()
    workletNodeRef.current?.disconnect()
    workletNodeRef.current = null
    sourceRef.current?.disconnect()
    sourceRef.current = null
    streamRef.current?.getTracks().forEach((track) => track.stop())
    streamRef.current = null
    void audioContextRef.current?.close()
    audioContextRef.current = null
    micAnalyserRef.current = null
    micFrequencyDataRef.current = null
  }, [])

  const closeSocket = useCallback(() => {
    socketRef.current?.close()
    socketRef.current = null
  }, [])

  /** One attempt to mint a token and open the ElevenLabs WebSocket — no retry inside this
   * function; {@link connectWithRetry} owns the bounded retry budget (research.md Decision 8).
   * `'refused'` when our own server answered that no session can be had (switched off, not
   * configured): that's a decision, not a blip, so it isn't retried. */
  const connectOnce = useCallback(async (): Promise<WebSocket | null | 'refused'> => {
    let session: Awaited<ReturnType<typeof createSttSession>>
    try {
      session = await createSttSession(language)
    } catch (err) {
      return err instanceof ApiError ? 'refused' : null
    }

    try {
      const socket = new WebSocket(
        `wss://api.elevenlabs.io/v1/speech-to-text/realtime?token=${encodeURIComponent(session.token)}&model_id=scribe_v2_realtime&audio_format=pcm_16000`,
      )

      await new Promise<void>((resolve, reject) => {
        const onOpen = () => {
          socket.removeEventListener('error', onError)
          resolve()
        }
        const onError = () => {
          socket.removeEventListener('open', onOpen)
          reject(new Error('Voice provider connection failed'))
        }
        socket.addEventListener('open', onOpen, { once: true })
        socket.addEventListener('error', onError, { once: true })
      })

      return socket
    } catch {
      return null
    }
  }, [language])

  const connectWithRetry = useCallback(async (): Promise<WebSocket | null> => {
    for (let attempt = 0; attempt <= MAX_RECONNECT_ATTEMPTS; attempt++) {
      const socket = await connectOnce()
      if (socket === 'refused') break
      if (socket) return socket
      if (attempt < MAX_RECONNECT_ATTEMPTS) {
        await new Promise((resolve) => setTimeout(resolve, RECONNECT_DELAY_MS))
      }
    }

    // FR-004/FR-033 boundary (research.md Decision 8): retries exhausted (or the server refused
    // outright) — this is no longer a transient blip, hand off to the fallback engine.
    failOver()
    return null
  }, [connectOnce, failOver])

  const commit = useCallback(() => {
    clearSilenceTimer()
    const socket = socketRef.current
    if (socket && socket.readyState === WebSocket.OPEN) {
      // No standalone "commit" message exists — finalizing means sending an `input_audio_chunk`
      // with `commit: true`; an empty-audio one when (as here) there's no new audio to attach it
      // to, e.g. the silence-timeout auto-commit.
      socket.send(
        JSON.stringify({
          message_type: 'input_audio_chunk',
          audio_base_64: '',
          sample_rate: SAMPLE_RATE_HZ,
          commit: true,
        }),
      )
    }
  }, [])

  const scheduleAutoCommit = useCallback(() => {
    clearSilenceTimer()
    silenceTimerRef.current = setTimeout(() => {
      commit()
    }, SILENCE_COMMIT_DELAY_MS)
  }, [commit])

  const attachSocketHandlers = useCallback(
    (socket: WebSocket) => {
      socket.addEventListener('message', (event) => {
        try {
          const data = JSON.parse(event.data as string) as {
            message_type?: string
            text?: string
          }
          if (data.message_type === 'partial_transcript' && data.text) {
            onPartialTranscript(data.text)
            if (modeRef.current === 'continuous') {
              scheduleAutoCommit()
            }
          } else if (data.message_type === 'committed_transcript' && data.text) {
            clearSilenceTimer()
            // Tear down the audio graph and socket immediately — the transcript is
            // complete. Without this, the old AudioWorklet keeps reading socketRef.current
            // (which start() will update to the next session's socket) and feeds the
            // new session's WebSocket with the old stream's audio, causing ElevenLabs to
            // receive double audio and produce garbled/cut-off transcripts.
            cleanupAudioGraph()
            closeSocket()
            setIsListening(false)
            onFinalTranscript(data.text)
          }
        } catch {
          fail('Received an unreadable message from the voice provider.')
        }
      })

      socket.addEventListener('close', () => {
        if (socketRef.current === socket) {
          socketRef.current = null
        }
      })
    },
    [onPartialTranscript, onFinalTranscript, scheduleAutoCommit, cleanupAudioGraph, closeSocket, fail],
  )

  /** Microphone source plus the analyser behind `getMicIntensity` — every engine shares it. */
  const buildMicGraph = useCallback((stream: MediaStream) => {
    const audioContext = new AudioContext()
    audioContextRef.current = audioContext
    const source = audioContext.createMediaStreamSource(stream)
    sourceRef.current = source

    // Mic analyser — tapped before the worklet so the waveform reacts to the user's
    // voice during listening, not just during AI playback.
    const micAnalyser = audioContext.createAnalyser()
    micAnalyser.fftSize = 256
    source.connect(micAnalyser)
    micAnalyserRef.current = micAnalyser
    micFrequencyDataRef.current = new Uint8Array(new ArrayBuffer(micAnalyser.frequencyBinCount))
    return { audioContext, source, micAnalyser }
  }, [])

  /** Runs one utterance on a fallback engine over `stream`, which the caller has already opened. */
  const startFallback = useCallback(
    (stream: MediaStream) => {
      const pickEngine = (): FallbackEngine | null => {
        const unusable = unusableEnginesRef.current
        if (!unusable.has('whisper') && isWhisperDictationSupported()) return 'whisper'
        if (!unusable.has('browser') && getBrowserSpeechRecognition()) return 'browser'
        return null
      }

      const engine = pickEngine()
      if (!engine) {
        stream.getTracks().forEach((track) => track.stop())
        setEngineNotice(null)
        fail(
          'Live dictation is unavailable: ElevenLabs could not be reached, and this browser can neither record audio for Whisper nor recognise speech itself.',
        )
        return
      }

      streamRef.current = stream
      const { micAnalyser } = buildMicGraph(stream)

      const endTurn = () => {
        fallbackSessionRef.current = null
        cleanupAudioGraph()
        setIsListening(false)
      }

      const run = () => {
        const Recognition = getBrowserSpeechRecognition()
        const callbacks = {
          onPartial: (text: string) => {
            if (fallbackSessionRef.current === session) onPartialTranscript(text)
          },
          onFinal: (text: string) => {
            if (fallbackSessionRef.current !== session) return
            if (!text && modeRef.current === 'continuous') {
              // Heard a noise, not words — keep listening rather than ending the turn on nothing.
              run()
              return
            }
            endTurn()
            if (text) onFinalTranscript(text)
          },
          onError: (failure: DictationFailure) => {
            if (fallbackSessionRef.current !== session) return
            endTurn()
            if (failure.engineUnusable) unusableEnginesRef.current.add(engine)
            const next = failure.engineUnusable ? pickEngine() : null
            fail(
              next
                ? `${failure.message} Dictation will use ${next === 'browser' ? "your browser's speech recognition" : 'Whisper'} from your next turn.`
                : failure.message,
            )
          },
        }
        const session: DictationSession =
          engine === 'browser' && Recognition
            ? startBrowserDictation(Recognition, language, callbacks)
            : startWhisperDictation(stream, micAnalyser, callbacks)
        fallbackSessionRef.current = session
      }

      try {
        run()
      } catch (err) {
        endTurn()
        unusableEnginesRef.current.add(engine)
        fail(err instanceof Error && err.message ? err.message : 'The backup dictation engine could not start.')
        return
      }

      setEngineNotice(FALLBACK_NOTICES[engine])
      setIsListening(true)
    },
    [buildMicGraph, cleanupAudioGraph, fail, language, onFinalTranscript, onPartialTranscript],
  )

  const getMicIntensity = useCallback((): number => {
    const analyser = micAnalyserRef.current
    const data = micFrequencyDataRef.current
    if (!analyser || !data) return 0
    analyser.getByteFrequencyData(data)
    let sum = 0
    for (let i = 0; i < data.length; i++) sum += data[i]
    return Math.min(1, sum / data.length / 255)
  }, [])

  const start = useCallback(async () => {
    if (!isSupported) {
      fail('Voice input is not supported in this browser.')
      return
    }

    // Defensive cleanup — committed_transcript normally tears down the previous session
    // immediately, but start() may also be called while a session is unexpectedly still
    // active (e.g. network delay). Ensures no orphaned audio graph or socket persists.
    cleanupAudioGraph()
    closeSocket()

    setError(null)
    setDeviceNotice(null)

    // specs/033-hold-to-talk-and-echo-fix: explicit echoCancellation, not just a browser
    // default, as defense-in-depth alongside the primary fix (muting the input track outright
    // during AiSpeaking, via setInputMuted below) for the brief windows the mic is live near
    // Lucy's own audio.
    let audioConstraint: boolean | MediaTrackConstraints = { echoCancellation: true }
    if (preferredMicrophoneDeviceId) {
      try {
        const devices = await navigator.mediaDevices.enumerateDevices()
        const stillPresent = devices.some(
          (device) =>
            device.kind === 'audioinput' && device.deviceId === preferredMicrophoneDeviceId,
        )
        if (stillPresent) {
          audioConstraint = { echoCancellation: true, deviceId: { exact: preferredMicrophoneDeviceId } }
        } else {
          setDeviceNotice(
            'Your saved microphone is no longer available — using the default microphone instead.',
          )
        }
      } catch {
        // enumerateDevices itself failing (rare) — fall back to the default device silently
        // rather than blocking voice input entirely over a diagnostics call.
      }
    }

    let stream: MediaStream
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: audioConstraint })
      setPermissionState('granted')
    } catch {
      setPermissionState('denied')
      fail('Microphone access was denied. Check your browser’s site permissions and try again.')
      return
    }

    // Once failed over, stay on the fallback — the caller's probeRecoveryIfDegraded flips the
    // store back to primary as soon as ElevenLabs answers again.
    const socket =
      canStreamRealtime && useVoiceProviderStatus.getState().provider === 'primary' ? await connectWithRetry() : null
    if (!socket) {
      startFallback(stream)
      return
    }

    setEngineNotice(null)
    socketRef.current = socket
    attachSocketHandlers(socket)
    streamRef.current = stream

    const { audioContext, source } = buildMicGraph(stream)
    await audioContext.audioWorklet.addModule('/audio/recorder-worklet.js')

    const workletNode = new AudioWorkletNode(audioContext, 'recorder-worklet')
    workletNodeRef.current = workletNode

    workletNode.port.onmessage = (event: MessageEvent<Float32Array>) => {
      const chunk = event.data

      const downsampled = downsampleTo16kHz(chunk, audioContext.sampleRate)
      const pcm = float32ToInt16Pcm(downsampled)
      const currentSocket = socketRef.current
      if (currentSocket && currentSocket.readyState === WebSocket.OPEN) {
        currentSocket.send(
          JSON.stringify({
            message_type: 'input_audio_chunk',
            audio_base_64: toBase64(pcm),
            sample_rate: SAMPLE_RATE_HZ,
            commit: false,
          }),
        )
      }
    }

    source.connect(workletNode)
    setIsListening(true)
  }, [
    isSupported,
    canStreamRealtime,
    connectWithRetry,
    attachSocketHandlers,
    preferredMicrophoneDeviceId,
    cleanupAudioGraph,
    closeSocket,
    fail,
    startFallback,
    buildMicGraph,
  ])

  /** Manual end of capture (FR-006) — discards without waiting for a commit round trip. */
  const cancel = useCallback(() => {
    cleanupAudioGraph()
    closeSocket()
    setIsListening(false)
  }, [cleanupAudioGraph, closeSocket])

  const stop = useCallback(() => {
    // A fallback engine still has to transcribe what it heard; it ends the turn itself.
    if (fallbackSessionRef.current) {
      fallbackSessionRef.current.commit()
      return
    }
    commit()
    cleanupAudioGraph()
    closeSocket()
    setIsListening(false)
  }, [commit, cleanupAudioGraph, closeSocket])

  /** specs/033-hold-to-talk-and-echo-fix FR-009 — disables (not tears down) the active
   * stream's audio input while Lucy is speaking, so her own voice can never be picked up as
   * user speech; re-enabling resumes normal listening with no reconnect/graph-rebuild cost.
   * Safe no-op if no stream is currently active. */
  const setInputMuted = useCallback((muted: boolean) => {
    streamRef.current?.getAudioTracks().forEach((track) => {
      track.enabled = !muted
    })
  }, [])

  const clearError = useCallback(() => setError(null), [])
  const clearDeviceNotice = useCallback(() => setDeviceNotice(null), [])

  return {
    isSupported,
    isListening,
    permissionState,
    error,
    deviceNotice,
    engineNotice,
    start,
    stop,
    cancel,
    setInputMuted,
    getMicIntensity,
    clearError,
    clearDeviceNotice,
  }
}
