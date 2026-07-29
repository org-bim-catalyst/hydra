import { useCallback, useRef, useState } from 'react'

// The Web Speech API's SpeechRecognition type isn't in the standard DOM lib yet,
// and ships behind a vendor prefix in Chromium — narrowly typed here rather than `any`.
interface SpeechRecognitionResultLike {
  transcript: string
}
interface SpeechRecognitionEventLike extends Event {
  results: ArrayLike<ArrayLike<SpeechRecognitionResultLike>>
}
interface SpeechRecognitionErrorEventLike extends Event {
  error: string
}
interface SpeechRecognitionLike extends EventTarget {
  lang: string
  continuous: boolean
  interimResults: boolean
  start(): void
  stop(): void
  onresult: ((event: SpeechRecognitionEventLike) => void) | null
  onerror: ((event: SpeechRecognitionErrorEventLike) => void) | null
  onend: (() => void) | null
  onstart: (() => void) | null
}

/** Maps the Web Speech API's error codes to a message a user can actually act on. */
function describeError(code: string): string {
  switch (code) {
    case 'not-allowed':
    case 'permission-denied':
      return 'Microphone access was denied. Check your browser’s site permissions and try again.'
    case 'no-speech':
      return 'No speech detected. Try again.'
    case 'audio-capture':
      return 'No microphone was found.'
    case 'network':
      return 'A network error interrupted voice recognition.'
    default:
      return 'Voice input failed. Please try again.'
  }
}

type SpeechRecognitionConstructor = new () => SpeechRecognitionLike

function getSpeechRecognitionCtor(): SpeechRecognitionConstructor | undefined {
  const w = window as unknown as {
    SpeechRecognition?: SpeechRecognitionConstructor
    webkitSpeechRecognition?: SpeechRecognitionConstructor
  }
  return w.SpeechRecognition ?? w.webkitSpeechRecognition
}

// If the browser never resolves the microphone permission prompt (or silently blocks it at
// the OS level without ever showing one), `onstart` never fires and the recognition object
// just sits waiting forever with no feedback at all. This watchdog only guards that specific
// "never even started" window — once onstart fires, continuous listening with no speech yet
// is normal and left to the browser's own 'no-speech' handling, not second-guessed here.
//
// The 15s figure is a judgment call, not a validated one: this environment's headless
// Chromium never fires `onstart` at all, granted permission or not (it depends on a real
// speech-service session, not just getUserMedia), so the "stays quiet on a real working mic"
// path could not be tested here — only that the watchdog correctly catches the stuck case.
// Widen this further if real-world use shows it firing on connections that would've worked.
const PERMISSION_TIMEOUT_MS = 15000

/** Client-side voice input (FR-006) — unchanged from the legacy app's browser-native approach. */
export function useVoiceRecognition(lang: string) {
  const [isListening, setIsListening] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const recognitionRef = useRef<SpeechRecognitionLike | null>(null)
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const isSupported = getSpeechRecognitionCtor() !== undefined

  const clearWatchdog = () => {
    if (timeoutRef.current !== null) {
      clearTimeout(timeoutRef.current)
      timeoutRef.current = null
    }
  }

  const start = useCallback(
    (onResult: (transcript: string) => void) => {
      const Ctor = getSpeechRecognitionCtor()
      if (!Ctor) return

      setError(null)
      const recognition = new Ctor()
      recognition.lang = lang
      recognition.continuous = true
      recognition.interimResults = false

      recognition.onstart = () => clearWatchdog()
      recognition.onresult = (event) => {
        const last = event.results[event.results.length - 1][0]
        onResult(last.transcript)
      }
      recognition.onend = () => {
        clearWatchdog()
        setIsListening(false)
      }
      // Previously silent (just reverted the mic icon) — a permission denial, missing
      // microphone, or network hiccup all looked identical to "nothing happened."
      recognition.onerror = (event) => {
        clearWatchdog()
        setError(describeError(event.error))
        setIsListening(false)
      }

      recognitionRef.current = recognition
      recognition.start()
      setIsListening(true)

      timeoutRef.current = setTimeout(() => {
        recognition.stop()
        setError(
          'The browser never started listening — check for a hidden microphone permission prompt, or your system’s microphone privacy settings.',
        )
        setIsListening(false)
      }, PERMISSION_TIMEOUT_MS)
    },
    [lang],
  )

  const stop = useCallback(() => {
    clearWatchdog()
    recognitionRef.current?.stop()
    setIsListening(false)
  }, [])

  const clearError = useCallback(() => setError(null), [])

  return { isSupported, isListening, error, start, stop, clearError }
}
