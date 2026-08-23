import { useCallback, useRef, useState } from 'react'
import { transcribeAudio } from '../api/aiApi'
import type { MicrophonePermissionState } from './useSpeechRecognition'

export type RecordingPhase = 'idle' | 'recording' | 'transcribing'

const FFT_SIZE = 256

/**
 * specs/026-floating-chat-assistant FR-019–FR-023, specs/031-voice-controls-redesign
 * research.md #1/#2 — Push-to-Talk's record → stop-and-transcribe → cancel flow.
 * Deliberately independent of `useSpeechRecognition` (which streams audio to ElevenLabs
 * live the moment `start()` is called — a direct conflict with "no audio is transmitted
 * before the recording actually finishes"): this hook buffers captured audio locally via
 * `MediaRecorder` and only ever calls the existing `/ai/transcriptions` endpoint
 * (`transcribeAudio`, already used by `ChatComposer`'s file-attach path) from
 * {@link finish}, never from {@link start}/{@link cancel}.
 *
 * The live waveform is driven by a `Web Audio AnalyserNode` on the same raw
 * `getUserMedia` stream, mirroring `useVoiceAnalyzer.ts`'s established
 * ref-based-`getIntensity()`-polled-per-frame pattern (research.md #3) — never React
 * state per frame.
 */
export function useVoiceRecorder() {
  const [phase, setPhase] = useState<RecordingPhase>('idle')
  const [permissionState, setPermissionState] = useState<MicrophonePermissionState>('unknown')
  const [error, setError] = useState<string | null>(null)

  const streamRef = useRef<MediaStream | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const analyserRef = useRef<AnalyserNode | null>(null)
  const frequencyDataRef = useRef<Uint8Array<ArrayBuffer> | null>(null)
  const mediaRecorderRef = useRef<MediaRecorder | null>(null)
  const chunksRef = useRef<Blob[]>([])
  const phaseRef = useRef<RecordingPhase>('idle')

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

    chunksRef.current = []
    const recorder = new MediaRecorder(stream)
    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) chunksRef.current.push(event.data)
    }
    recorder.start()
    mediaRecorderRef.current = recorder

    setPhaseBoth('recording')
  }, [isSupported])

  /** specs/031-voice-controls-redesign FR-001/FR-002, research.md Decision 1 — stops
   * capture and immediately transcribes in one step (previously stopped into a separate
   * `'reviewing'` phase requiring a second, manual "send for transcription" action — the
   * confusing extra button removed by this feature). Awaits the recorder's `onstop` event
   * for the final blob, then submits it to the existing transcription endpoint and
   * resolves with the transcript, exactly as legacy voice-to-text input did. Resolves with
   * an empty string (and surfaces `error`, constitution §2.VIII) on failure. No-ops
   * (resolves `''`) if called outside `'recording'`. */
  const finish = useCallback(async (): Promise<string> => {
    if (phaseRef.current !== 'recording') return ''
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
    setPhaseBoth('transcribing')

    try {
      const file = new File([blob], 'recording.webm', { type: blob.type || 'audio/webm' })
      const transcript = await transcribeAudio(file)
      setPhaseBoth('idle')
      return transcript
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to transcribe the recording.')
      setPhaseBoth('idle')
      return ''
    }
  }, [cleanupAudioGraph])

  /** FR-004/FR-024: discards the captured audio from an in-progress `recording` and
   * never transmits it. Also the path a collapse mid-recording routes through. */
  const cancel = useCallback(() => {
    if (phaseRef.current === 'idle') return
    if (phaseRef.current === 'recording') {
      mediaRecorderRef.current?.stop()
    }
    mediaRecorderRef.current = null
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
    getIntensity,
    start,
    finish,
    cancel,
    clearError,
  }
}
