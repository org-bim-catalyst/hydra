import { ApiError } from '../../../api/httpClient'
import { transcribeAudio } from '../api/aiApi'
import { extensionForRecordingMimeType } from './useVoiceRecorder'

/**
 * The engines live dictation falls back to when ElevenLabs' realtime session can't be opened —
 * switched off under Admin → AI providers, unconfigured, or unreachable. In order of preference:
 *
 * 1. **Whisper** — the utterance is recorded locally and sent to `/ai/transcriptions` (the
 *    same endpoint Push-to-Talk uses) once a pause is heard. Consistent quality and language
 *    detection in every browser that can record audio; no partial transcripts.
 * 2. **The browser's own speech recognition** — used when Whisper can't record in this
 *    browser, or has already failed this session on the server side. Live partials, but the
 *    quality and language coverage are the browser vendor's.
 *
 * Each engine runs one utterance at a time as a {@link DictationSession}; `useSpeechRecognition`
 * owns the microphone, the audio graph and the choice between them.
 */
export type FallbackEngine = 'whisper' | 'browser'

export interface DictationSession {
  /** Finish now with what has been heard so far. */
  commit: () => void
  /** Discard the utterance; nothing is transcribed. Safe to call more than once. */
  cancel: () => void
}

export interface DictationFailure {
  message: string
  /** The engine itself is unusable (not configured, blocked, unsupported language) rather than
   * one utterance having failed — later turns should use the other engine. */
  engineUnusable: boolean
}

interface DictationCallbacks {
  onPartial: (text: string) => void
  /** The utterance's transcript; empty when nothing intelligible was said. */
  onFinal: (text: string) => void
  onError: (failure: DictationFailure) => void
}

/** Root-mean-square level (0–1) above which the microphone signal counts as speech. */
const SPEECH_LEVEL = 0.02
/** A longer pause than the realtime path's: Whisper has no partials to show the pause is
 * being heard, so ending an utterance too eagerly would cut a slow speaker off mid-sentence. */
const WHISPER_SILENCE_COMMIT_MS = 1200
/** Recording restarts after this long without speech, so an idle microphone never builds up
 * a long silent recording to upload once someone does speak. */
const IDLE_SEGMENT_MS = 30_000
const MAX_UTTERANCE_MS = 60_000
const LEVEL_POLL_MS = 50

export const isWhisperDictationSupported = () => typeof MediaRecorder !== 'undefined'

/**
 * Records `stream` until speech has been followed by {@link WHISPER_SILENCE_COMMIT_MS} of quiet
 * (or {@link commit} is called), then transcribes the recording with Whisper. `analyser` must be
 * fed by the same stream; its time-domain signal is how the pause is detected.
 */
export function startWhisperDictation(
  stream: MediaStream,
  analyser: AnalyserNode,
  { onPartial, onFinal, onError }: DictationCallbacks,
): DictationSession {
  const samples = new Float32Array(analyser.fftSize)
  let chunks: Blob[] = []
  let recorder = startRecorder()
  let segmentStartedAt = Date.now()
  let heardSpeechAt: number | null = null
  let lastSpeechAt = 0
  let settled = false

  function startRecorder() {
    const next = new MediaRecorder(stream)
    next.ondataavailable = (event) => {
      if (event.data.size > 0) chunks.push(event.data)
    }
    next.start()
    return next
  }

  function discardRecorder() {
    recorder.ondataavailable = null
    recorder.onstop = null
    if (recorder.state !== 'inactive') recorder.stop()
  }

  const poll = setInterval(() => {
    analyser.getFloatTimeDomainData(samples)
    let sum = 0
    for (const sample of samples) sum += sample * sample
    const level = Math.sqrt(sum / samples.length)
    const now = Date.now()

    if (level >= SPEECH_LEVEL) {
      if (heardSpeechAt === null) {
        heardSpeechAt = now
        // No words until Whisper answers; an empty partial still tells the caller someone is talking.
        onPartial('')
      }
      lastSpeechAt = now
    }

    if (heardSpeechAt !== null) {
      if (now - lastSpeechAt >= WHISPER_SILENCE_COMMIT_MS || now - heardSpeechAt >= MAX_UTTERANCE_MS) commit()
    } else if (now - segmentStartedAt >= IDLE_SEGMENT_MS) {
      discardRecorder()
      chunks = []
      recorder = startRecorder()
      segmentStartedAt = now
    }
  }, LEVEL_POLL_MS)

  function commit() {
    if (settled) return
    settled = true
    clearInterval(poll)

    if (heardSpeechAt === null) {
      discardRecorder()
      onFinal('')
      return
    }

    const finished = recorder
    finished.onstop = () => {
      const mimeType = finished.mimeType || 'audio/webm'
      const file = new File([new Blob(chunks, { type: mimeType })], `dictation.${extensionForRecordingMimeType(mimeType)}`, {
        type: mimeType,
      })
      transcribeAudio(file).then(
        (text) => onFinal(text.trim()),
        (err: unknown) =>
          onError({
            message: err instanceof Error && err.message ? err.message : 'Whisper could not transcribe what you said.',
            // A 5xx is the server saying transcription itself is down or unconfigured; anything
            // else (a dropped connection, an oversized clip) is this utterance's problem.
            engineUnusable: err instanceof ApiError && err.status >= 500,
          }),
      )
    }
    finished.stop()
  }

  return {
    commit,
    cancel: () => {
      if (settled) return
      settled = true
      clearInterval(poll)
      discardRecorder()
    },
  }
}

/** The subset of the Web Speech API's `SpeechRecognition` used here — TypeScript's DOM library
 * no longer declares the interface itself, only its events. */
interface BrowserSpeechRecognition {
  lang: string
  continuous: boolean
  interimResults: boolean
  onresult: ((event: SpeechRecognitionEvent) => void) | null
  onerror: ((event: SpeechRecognitionErrorEvent) => void) | null
  onend: (() => void) | null
  start: () => void
  stop: () => void
  abort: () => void
}

type BrowserSpeechRecognitionConstructor = new () => BrowserSpeechRecognition

export function getBrowserSpeechRecognition(): BrowserSpeechRecognitionConstructor | undefined {
  if (typeof window === 'undefined') return undefined
  const candidates = window as unknown as {
    SpeechRecognition?: BrowserSpeechRecognitionConstructor
    webkitSpeechRecognition?: BrowserSpeechRecognitionConstructor
  }
  return candidates.SpeechRecognition ?? candidates.webkitSpeechRecognition
}

function browserFailure(error: string, language: string): DictationFailure {
  switch (error) {
    case 'not-allowed':
    case 'service-not-allowed':
      return {
        message: "Your browser blocked its speech recognition. Allow microphone access for this site and try again.",
        engineUnusable: true,
      }
    case 'language-not-supported':
      return {
        message: `Your browser's speech recognition doesn't support this language (${language}).`,
        engineUnusable: true,
      }
    case 'network':
      return {
        message: "Your browser's speech recognition couldn't reach its service. Check your connection and try again.",
        engineUnusable: false,
      }
    default:
      return { message: `Your browser's speech recognition stopped unexpectedly (${error}).`, engineUnusable: false }
  }
}

/**
 * One utterance through the browser's own recognizer. A recognizer that ends on silence with
 * nothing heard is restarted, so the microphone keeps listening the way the realtime path does.
 */
export function startBrowserDictation(
  Recognition: BrowserSpeechRecognitionConstructor,
  language: string,
  { onPartial, onFinal, onError }: DictationCallbacks,
): DictationSession {
  const recognition = new Recognition()
  recognition.lang = language
  recognition.continuous = false
  recognition.interimResults = true

  let latest = ''
  let committing = false
  let settled = false

  const settle = () => {
    settled = true
    recognition.onresult = null
    recognition.onerror = null
    recognition.onend = null
  }

  recognition.onresult = (event) => {
    let text = ''
    let isFinal = false
    for (let index = 0; index < event.results.length; index++) {
      const result = event.results[index]
      text += result[0]?.transcript ?? ''
      isFinal ||= result.isFinal
    }
    latest = text.trim()
    if (isFinal) {
      settle()
      onFinal(latest)
    } else if (latest) {
      onPartial(latest)
    }
  }

  recognition.onerror = (event) => {
    // Silence and our own abort() both end the session normally; onend decides what's next.
    if (event.error === 'no-speech' || event.error === 'aborted') return
    settle()
    onError(browserFailure(event.error, language))
  }

  recognition.onend = () => {
    if (committing) {
      settle()
      onFinal(latest)
      return
    }
    try {
      recognition.start()
    } catch (err) {
      settle()
      onError({
        message: err instanceof Error && err.message ? err.message : "Your browser's speech recognition could not restart.",
        engineUnusable: false,
      })
    }
  }

  recognition.start()

  return {
    commit: () => {
      if (settled || committing) return
      committing = true
      recognition.stop()
    },
    cancel: () => {
      if (settled) return
      settle()
      recognition.abort()
    },
  }
}
