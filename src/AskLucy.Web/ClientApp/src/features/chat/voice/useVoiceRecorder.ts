import { useCallback, useRef, useState } from 'react'
import { transcribeDictationClip } from '../api/aiApi'
import { createSttSession } from '../api/voiceApi'
import {
  getBrowserSpeechRecognition,
  startBrowserDictation,
  type DictationSession,
} from './dictationFallback'
import type { MicrophonePermissionState } from './useSpeechRecognition'
import { toWav16kMono } from './wavEncoder'

export type RecordingPhase = 'idle' | 'recording' | 'transcribing'

/** contracts/dictation-session.md — the clip engine the turn resolved to, remembered from
 * `start()` through `finish()`/`cancel()`. `'Realtime'` never legitimately reaches Push-to-Talk
 * (FR-017: ElevenLabs realtime cannot take a recorded clip), so it is treated as unsupported. */
type PushToTalkEngine = 'Clip' | 'Browser'

const BROWSER_ENGINE_NOTICE = "Dictation is unavailable — using your browser's speech recognition instead."

const FFT_SIZE = 256

/**
 * specs/026-floating-chat-assistant FR-019–FR-023, specs/031-voice-controls-redesign
 * research.md #1/#2, specs/078-restore-local-whisper — Push-to-Talk's record →
 * stop-and-transcribe → cancel flow. Deliberately independent of `useSpeechRecognition`
 * (which streams audio to ElevenLabs live the moment `start()` is called — a direct
 * conflict with "no audio is transmitted before the recording actually finishes"): this
 * hook buffers captured audio locally.
 *
 * `start()` first calls {@link createSttSession} with `mode: 'PushToTalk'`
 * (contracts/dictation-session.md) to learn which engine this turn should use, *before*
 * opening the microphone:
 * - `'Clip'` (Local Whisper or OpenAI Whisper): records via `MediaRecorder` as before, then
 *   {@link finish} converts the clip to 16 kHz mono WAV (`wavEncoder.ts`) and posts it to
 *   `transcribeDictationClip` (contracts/dictation-transcription.md) — never the legacy
 *   `/ai/transcriptions` endpoint, which dictation no longer uses.
 * - `'Browser'`: dictates through the browser's own recognizer (`dictationFallback.ts`,
 *   shared with `useSpeechRecognition`'s fallback path) from the start. `degraded` gates
 *   {@link engineNotice}: normal (no model deployed yet, FR-002) shows nothing; a genuine
 *   failover shows one.
 * A failed `stt-session` call itself (the server unreachable) falls back the same way —
 * FR-005b: dictation never fails over to a paid engine, only to the browser built-in.
 *
 * The live waveform is driven by a `Web Audio AnalyserNode` on the same raw
 * `getUserMedia` stream, mirroring `useVoiceAnalyzer.ts`'s established
 * ref-based-`getIntensity()`-polled-per-frame pattern (research.md #3) — never React
 * state per frame.
 */
export function useVoiceRecorder(language?: string) {
  const [phase, setPhase] = useState<RecordingPhase>('idle')
  const [permissionState, setPermissionState] = useState<MicrophonePermissionState>('unknown')
  const [error, setError] = useState<string | null>(null)
  const [engineNotice, setEngineNotice] = useState<string | null>(null)

  const streamRef = useRef<MediaStream | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const analyserRef = useRef<AnalyserNode | null>(null)
  const frequencyDataRef = useRef<Uint8Array<ArrayBuffer> | null>(null)
  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const chunksRef = useRef<Blob[]>([])
  const phaseRef = useRef<RecordingPhase>('idle')
  const engineRef = useRef<PushToTalkEngine | null>(null)
  const browserSessionRef = useRef<DictationSession | null>(null)
  const browserTranscriptResolveRef = useRef<((text: string) => void) | null>(null)
  const browserTranscriptPromiseRef = useRef<Promise<string> | null>(null)

  const isSupported =
    typeof navigator !== 'undefined' &&
    !!navigator.mediaDevices?.getUserMedia &&
    typeof MediaRecorder !== 'undefined' &&
    typeof AudioContext !== 'undefined'

  const setPhaseBoth = (next: RecordingPhase) => {
    phaseRef.current = next
    setPhase(next)
  }

  /** FR-024: torn down whenever capture ends, however it ends (finish, cancel, or an
   * external collapse-triggered cancel) — the mic is never left open once nothing is
   * actively being recorded. */
  const cleanupAudioGraph = useCallback(() => {
    analyserRef.current?.disconnect()
    analyserRef.current = null
    frequencyDataRef.current = null
    void audioContextRef.current?.close()
    audioContextRef.current = null
    streamRef.current?.getTracks().forEach((track) => track.stop())
    streamRef.current = null
  }, [])

  const start = useCallback(async () => {
    if (!isSupported) {
      setError('Voice recording is not supported in this browser.')
      return
    }
    if (phaseRef.current !== 'idle') return
    setError(null)

    // contracts/dictation-session.md — resolved before the microphone opens (FR-005a). A
    // failed request itself (server unreachable) is not retried: it falls back to the
    // browser built-in the same way an explicit `Browser` answer does (FR-005b).
    const session = await createSttSession(language ?? 'en', 'PushToTalk').catch(
      () => ({ engine: 'Browser' as const, token: null, expiresAtUtc: null, degraded: true }),
    )

    if (session.engine === 'Realtime') {
      // FR-017/research D4: Push-to-Talk never legitimately resolves to ElevenLabs realtime —
      // it cannot take a recorded clip. Fail closed rather than silently dropping the audio.
      setError('Dictation is not available.')
      return
    }

    const Recognition = session.engine === 'Browser' ? getBrowserSpeechRecognition() : undefined
    if (session.engine === 'Browser' && !Recognition) {
      setError('Voice recording is not supported in this browser.')
      return
    }

    let stream: MediaStream
    try {
      stream = await navigator.mediaDevices.getUserMedia({ audio: true })
      setPermissionState('granted')
    } catch {
      setPermissionState('denied')
      setError('Microphone access was denied. Check your browser’s site permissions and try again.')
      return
    }
    streamRef.current = stream

    const audioContext = new AudioContext()
    audioContextRef.current = audioContext
    const source = audioContext.createMediaStreamSource(stream)
    const analyser = audioContext.createAnalyser()
    analyser.fftSize = FFT_SIZE
    source.connect(analyser)
    analyserRef.current = analyser
    frequencyDataRef.current = new Uint8Array(new ArrayBuffer(analyser.frequencyBinCount))

    setEngineNotice(session.degraded ? BROWSER_ENGINE_NOTICE : null)

    if (session.engine === 'Browser') {
      engineRef.current = 'Browser'
      browserTranscriptPromiseRef.current = new Promise<string>((resolve) => {
        browserTranscriptResolveRef.current = resolve
      })
      browserSessionRef.current = startBrowserDictation(Recognition!, language ?? 'en', {
        onPartial: () => {},
        onFinal: (text) => {
          browserTranscriptResolveRef.current?.(text)
          browserTranscriptResolveRef.current = null
        },
        onError: (failure) => {
          setError(failure.message)
          browserTranscriptResolveRef.current?.('')
          browserTranscriptResolveRef.current = null
        },
      })
    } else {
      engineRef.current = 'Clip'
      chunksRef.current = []
      const recorder = new MediaRecorder(stream)
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) chunksRef.current.push(event.data)
      }
      recorder.start()
      mediaRecorderRef.current = recorder
    }

    setPhaseBoth('recording')
  }, [isSupported, language])

  /** specs/031-voice-controls-redesign FR-001/FR-002, research.md Decision 1 — stops
   * capture and immediately transcribes in one step (previously stopped into a separate
   * `'reviewing'` phase requiring a second, manual "send for transcription" action — the
   * confusing extra button removed by this feature). Awaits the recorder's `onstop` event
   * for the final blob, then submits it to the existing transcription endpoint and
   * resolves with the transcript, exactly as legacy voice-to-text input did. Resolves with
   * an empty string (and surfaces `error`, constitution §2.VIII) on failure. No-ops
   * (resolves `''`) if called outside `'recording'`. */
  /** Ends the `Browser` engine's session and resolves with what it heard, settling the promise
   * `start()` created — `onError` above already resolved it (with `''`) if the engine failed. */
  const finishBrowserEngine = useCallback(async (): Promise<string> => {
    browserSessionRef.current?.commit()
    const transcript = (await browserTranscriptPromiseRef.current) ?? ''
    browserSessionRef.current = null
    browserTranscriptPromiseRef.current = null
    engineRef.current = null
    return transcript
  }, [])

  const finish = useCallback(async (): Promise<string> => {
    if (phaseRef.current !== 'recording') return ''

    if (engineRef.current === 'Browser') {
      setPhaseBoth('transcribing')
      const transcript = await finishBrowserEngine()
      cleanupAudioGraph()
      setPhaseBoth('idle')
      return transcript
    }

    const recorder = mediaRecorderRef.current
    if (!recorder) return ''

    const blob = await new Promise<Blob>((resolve) => {
      recorder.onstop = () => {
        resolve(new Blob(chunksRef.current, { type: recorder.mimeType || 'audio/webm' }))
      }
      recorder.stop()
    })
    // Capture is done the moment the user says "finished speaking" — release the mic
    // immediately rather than holding it open through transcription.
    cleanupAudioGraph()
    mediaRecorderRef.current = null
    chunksRef.current = []
    engineRef.current = null
    setPhaseBoth('transcribing')

    try {
      const wav = await toWav16kMono(blob)
      if (!wav.converted) {
        // The clip couldn't be decoded — the server would refuse it as an invalid WAV anyway
        // (contracts/dictation-transcription.md), so this turn moves straight to the notice
        // rather than round-tripping a request that can only fail.
        setEngineNotice(BROWSER_ENGINE_NOTICE)
        setPhaseBoth('idle')
        return ''
      }
      const result = await transcribeDictationClip(wav.blob, language)
      setPhaseBoth('idle')
      return result.text
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to transcribe the recording.')
      setPhaseBoth('idle')
      return ''
    }
  }, [cleanupAudioGraph, finishBrowserEngine, language])

  /** FR-004/FR-024: discards the captured audio from an in-progress `recording` and
   * never transmits it. Also the path a collapse mid-recording routes through. */
  const cancel = useCallback(() => {
    if (phaseRef.current === 'idle') return
    if (engineRef.current === 'Browser') {
      browserSessionRef.current?.cancel()
      browserSessionRef.current = null
      browserTranscriptPromiseRef.current = null
    } else if (phaseRef.current === 'recording') {
      mediaRecorderRef.current?.stop()
    }
    mediaRecorderRef.current = null
    engineRef.current = null
    cleanupAudioGraph()
    chunksRef.current = []
    setPhaseBoth('idle')
  }, [cleanupAudioGraph])

  /** Ref-based — read every animation frame by `VoiceAnalyzer`, never via React state
   * (research.md #3). Zero once nothing is actively being captured. */
  const getIntensity = useCallback((): number => {
    const analyser = analyserRef.current
    const data = frequencyDataRef.current
    if (!analyser || !data) return 0
    analyser.getByteFrequencyData(data)
    let sum = 0
    for (let i = 0; i < data.length; i++) sum += data[i]
    return Math.min(1, sum / data.length / 255)
  }, [])

  const clearError = useCallback(() => setError(null), [])

  return {
    phase,
    isSupported,
    permissionState,
    error,
    engineNotice,
    getIntensity,
    start,
    finish,
    cancel,
    clearError,
  }
}
