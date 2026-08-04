import { useCallback, useEffect, useRef, useState } from 'react'
import { createSttSession } from '../api/voiceApi'
import { downsampleTo16kHz, float32ToInt16Pcm, toBase64 } from './pcm16'
import { useVoiceProviderStatus } from './voiceProviderStatus'

const MAX_RECONNECT_ATTEMPTS = 2 // research.md Decision 8
const RECONNECT_DELAY_MS = 1000
const SILENCE_COMMIT_DELAY_MS = 800 // FR-002: pause before auto-processing
const LOCAL_SPEECH_RMS_THRESHOLD = 0.02 // research.md Decision 10's fast local pre-trigger
const SAMPLE_RATE_HZ = 16000 // matches downsampleTo16kHz below and the `pcm_16000` audio_format

export type ConversationMode = 'push-to-talk' | 'continuous'
export type MicrophonePermissionState = 'unknown' | 'granted' | 'denied'

interface UseSpeechRecognitionOptions {
  language: string
  mode: ConversationMode
  onPartialTranscript: (text: string) => void
  onFinalTranscript: (text: string) => void
  /** research.md Decision 10 — fires the instant local audio crosses the amplitude
   * threshold, well before the authoritative transcript confirms real speech. The caller
   * (`useConversationAudio.ts`) uses this to duck AI playback immediately (US3). */
  onLocalSpeechLikely?: () => void
  /** FR-031: a previously saved microphone device id, if any. Checked against
   * `navigator.mediaDevices.enumerateDevices()` at session start — falls back to the
   * platform default (and surfaces {@link deviceNotice}) rather than failing outright when
   * the device is no longer present (e.g. unplugged). */
  preferredMicrophoneDeviceId?: string | null
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
 */
export function useSpeechRecognition({
  language,
  mode,
  onPartialTranscript,
  onFinalTranscript,
  onLocalSpeechLikely,
  preferredMicrophoneDeviceId,
}: UseSpeechRecognitionOptions) {
  const [isListening, setIsListening] = useState(false)
  const [permissionState, setPermissionState] = useState<MicrophonePermissionState>('unknown')
  const [error, setError] = useState<string | null>(null)
  const [deviceNotice, setDeviceNotice] = useState<string | null>(null)

  const socketRef = useRef<WebSocket | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const workletNodeRef = useRef<AudioWorkletNode | null>(null)
  const sourceRef = useRef<MediaStreamAudioSourceNode | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const silenceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const modeRef = useRef(mode)
  useEffect(() => {
    modeRef.current = mode
  }, [mode])

  const { failOver } = useVoiceProviderStatus()

  const isSupported =
    typeof navigator !== 'undefined' &&
    !!navigator.mediaDevices?.getUserMedia &&
    typeof AudioContext !== 'undefined' &&
    typeof AudioWorkletNode !== 'undefined' &&
    typeof WebSocket !== 'undefined'

  const clearSilenceTimer = () => {
    if (silenceTimerRef.current) {
      clearTimeout(silenceTimerRef.current)
      silenceTimerRef.current = null
    }
  }

  const cleanupAudioGraph = useCallback(() => {
    clearSilenceTimer()
    workletNodeRef.current?.port.close()
    workletNodeRef.current?.disconnect()
    workletNodeRef.current = null
    sourceRef.current?.disconnect()
    sourceRef.current = null
    streamRef.current?.getTracks().forEach((track) => track.stop())
    streamRef.current = null
    void audioContextRef.current?.close()
    audioContextRef.current = null
  }, [])

  const closeSocket = useCallback(() => {
    socketRef.current?.close()
    socketRef.current = null
  }, [])

  /** One attempt to mint a token and open the ElevenLabs WebSocket — no retry inside this
   * function; {@link connectWithRetry} owns the bounded retry budget (research.md Decision 8). */
  const connectOnce = useCallback(async (): Promise<WebSocket | null> => {
    try {
      const session = await createSttSession(language)
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
      if (socket) return socket
      if (attempt < MAX_RECONNECT_ATTEMPTS) {
        await new Promise((resolve) => setTimeout(resolve, RECONNECT_DELAY_MS))
      }
    }

    // FR-004/FR-033 boundary (research.md Decision 8): retries exhausted — this is no longer
    // a transient blip, hand off to the fallback engine.
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
            onFinalTranscript(data.text)
          }
        } catch {
          setError('Received an unreadable message from the voice provider.')
        }
      })

      socket.addEventListener('close', () => {
        if (socketRef.current === socket) {
          socketRef.current = null
        }
      })
    },
    [onPartialTranscript, onFinalTranscript, scheduleAutoCommit],
  )

  const start = useCallback(async () => {
    if (!isSupported) {
      setError('Voice input is not supported in this browser.')
      return
    }

    setError(null)
    setDeviceNotice(null)

    let audioConstraint: boolean | MediaTrackConstraints = true
    if (preferredMicrophoneDeviceId) {
      try {
        const devices = await navigator.mediaDevices.enumerateDevices()
        const stillPresent = devices.some(
          (device) =>
            device.kind === 'audioinput' && device.deviceId === preferredMicrophoneDeviceId,
        )
        if (stillPresent) {
          audioConstraint = { deviceId: { exact: preferredMicrophoneDeviceId } }
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
      setError('Microphone access was denied. Check your browser’s site permissions and try again.')
      return
    }

    const socket = await connectWithRetry()
    if (!socket) {
      stream.getTracks().forEach((track) => track.stop())
      setError('Voice recognition is temporarily using a reduced-quality fallback.')
      return
    }

    socketRef.current = socket
    attachSocketHandlers(socket)
    streamRef.current = stream

    const audioContext = new AudioContext()
    audioContextRef.current = audioContext
    await audioContext.audioWorklet.addModule('/audio/recorder-worklet.js')

    const source = audioContext.createMediaStreamSource(stream)
    sourceRef.current = source

    const workletNode = new AudioWorkletNode(audioContext, 'recorder-worklet')
    workletNodeRef.current = workletNode

    workletNode.port.onmessage = (event: MessageEvent<Float32Array>) => {
      const chunk = event.data

      let peak = 0
      for (const sample of chunk) {
        const abs = Math.abs(sample)
        if (abs > peak) peak = abs
      }
      if (peak > LOCAL_SPEECH_RMS_THRESHOLD) {
        onLocalSpeechLikely?.()
      }

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
    connectWithRetry,
    attachSocketHandlers,
    onLocalSpeechLikely,
    preferredMicrophoneDeviceId,
  ])

  /** Manual end of capture (FR-006) — discards without waiting for a commit round trip. */
  const cancel = useCallback(() => {
    cleanupAudioGraph()
    closeSocket()
    setIsListening(false)
  }, [cleanupAudioGraph, closeSocket])

  const stop = useCallback(() => {
    commit()
    cleanupAudioGraph()
    closeSocket()
    setIsListening(false)
  }, [commit, cleanupAudioGraph, closeSocket])

  const clearError = useCallback(() => setError(null), [])
  const clearDeviceNotice = useCallback(() => setDeviceNotice(null), [])

  return {
    isSupported,
    isListening,
    permissionState,
    error,
    deviceNotice,
    start,
    stop,
    cancel,
    clearError,
    clearDeviceNotice,
  }
}
