import { useCallback, useRef, useState } from 'react'
import { transcribeAudio } from '../api/aiApi'
import type { MicrophonePermissionState } from './useSpeechRecognition'

export type RecordingPhase = 'idle' | 'recording' | 'reviewing' | 'transcribing'

const FFT_SIZE = 256

/**
 * specs/026-floating-chat-assistant FR-019–FR-023, research.md #2 — Push-to-Talk's
 * discrete record → review → cancel/accept-to-transcribe flow. Deliberately independent
 * of `useSpeechRecognition` (which streams audio to ElevenLabs live the moment `start()`
 * is called — a direct conflict with FR-019/FR-021's "no audio is transmitted before
 * explicit accept"): this hook buffers captured audio locally via `MediaRecorder` and
 * only ever calls the existing `/ai/transcriptions` endpoint (`transcribeAudio`, already
 * used by `ChatComposer`'s file-attach path) from {@link accept}, never from
 * {@link start}/{@link finish}/{@link cancel}.
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
  const blobRef = useRef<Blob | null>(null)
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
    blobRef.current = null
    const recorder = new MediaRecorder(stream)
    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) chunksRef.current.push(event.data)
    }
    recorder.start()
    mediaRecorderRef.current = recorder

    setPhaseBoth('recording')
  }, [isSupported])

  /** FR-020: stops capture and moves into the review state — still without transmitting
   * the audio anywhere (only {@link accept} does that). */
  const finish = useCallback(() => {
    if (phaseRef.current !== 'recording') return
    const recorder = mediaRecorderRef.current
    if (!recorder) return

    recorder.onstop = () => {
      blobRef.current = new Blob(chunksRef.current, { type: recorder.mimeType || 'audio/webm' })
      setPhaseBoth('reviewing')
    }
    recorder.stop()
    // Capture is done the moment the user says "finished speaking" — release the mic
    // immediately rather than holding it open through the review step.
    cleanupAudioGraph()
  }, [cleanupAudioGraph])

  /** FR-021/FR-024: discards the captured audio — from `recording` (an in-progress hold)
   * or `reviewing` (after finish) — and never transmits it. Also the path a collapse
   * mid-recording/review routes through. */
  const cancel = useCallback(() => {
    if (phaseRef.current === 'idle') return
    if (phaseRef.current === 'recording') {
      mediaRecorderRef.current?.stop()
    }
    mediaRecorderRef.current = null
    cleanupAudioGraph()
    chunksRef.current = []
    blobRef.current = null
    setPhaseBoth('idle')
  }, [cleanupAudioGraph])

  /** FR-022: the *only* action that transmits the recording — submits it to the existing
   * transcription endpoint and resolves with the transcript, exactly as existing
   * voice-to-text input is used today. Resolves with an empty string (and surfaces
   * `error`, constitution §2.VIII) if called outside `reviewing` or on failure. */
  const accept = useCallback(async (): Promise<string> => {
    if (phaseRef.current !== 'reviewing' || !blobRef.current) return ''
    setPhaseBoth('transcribing')
    try {
      const file = new File([blobRef.current], 'recording.webm', {
        type: blobRef.current.type || 'audio/webm',
      })
      const transcript = await transcribeAudio(file)
      blobRef.current = null
      setPhaseBoth('idle')
      return transcript
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to transcribe the recording.')
      blobRef.current = null
      setPhaseBoth('idle')
      return ''
    }
  }, [])

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
    accept,
    clearError,
  }
}
