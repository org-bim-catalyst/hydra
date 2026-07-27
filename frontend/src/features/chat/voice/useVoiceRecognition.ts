import { useCallback, useRef, useState } from 'react'

// The Web Speech API's SpeechRecognition type isn't in the standard DOM lib yet,
// and ships behind a vendor prefix in Chromium — narrowly typed here rather than `any`.
interface SpeechRecognitionResultLike {
  transcript: string
}
interface SpeechRecognitionEventLike extends Event {
  results: ArrayLike<ArrayLike<SpeechRecognitionResultLike>>
}
interface SpeechRecognitionLike extends EventTarget {
  lang: string
  continuous: boolean
  interimResults: boolean
  start(): void
  stop(): void
  onresult: ((event: SpeechRecognitionEventLike) => void) | null
  onerror: ((event: Event) => void) | null
  onend: (() => void) | null
}

type SpeechRecognitionConstructor = new () => SpeechRecognitionLike

function getSpeechRecognitionCtor(): SpeechRecognitionConstructor | undefined {
  const w = window as unknown as {
    SpeechRecognition?: SpeechRecognitionConstructor
    webkitSpeechRecognition?: SpeechRecognitionConstructor
  }
  return w.SpeechRecognition ?? w.webkitSpeechRecognition
}

/** Client-side voice input (FR-006) — unchanged from the legacy app's browser-native approach. */
export function useVoiceRecognition(lang: string) {
  const [isListening, setIsListening] = useState(false)
  const recognitionRef = useRef<SpeechRecognitionLike | null>(null)

  const isSupported = getSpeechRecognitionCtor() !== undefined

  const start = useCallback(
    (onResult: (transcript: string) => void) => {
      const Ctor = getSpeechRecognitionCtor()
      if (!Ctor) return

      const recognition = new Ctor()
      recognition.lang = lang
      recognition.continuous = true
      recognition.interimResults = false

      recognition.onresult = (event) => {
        const last = event.results[event.results.length - 1][0]
        onResult(last.transcript)
      }
      recognition.onend = () => setIsListening(false)
      recognition.onerror = () => setIsListening(false)

      recognitionRef.current = recognition
      recognition.start()
      setIsListening(true)
    },
    [lang],
  )

  const stop = useCallback(() => {
    recognitionRef.current?.stop()
    setIsListening(false)
  }, [])

  return { isSupported, isListening, start, stop }
}
