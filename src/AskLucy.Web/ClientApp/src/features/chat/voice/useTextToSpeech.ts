import { useCallback } from 'react'

/** Client-side voice output (FR-006) via the browser's native SpeechSynthesis API. */
export function useTextToSpeech() {
  const isSupported = typeof window !== 'undefined' && 'speechSynthesis' in window

  const speak = useCallback(
    (text: string, lang: string) => {
      if (!isSupported) return

      const utterance = new SpeechSynthesisUtterance(text)
      utterance.lang = lang

      const voice = window.speechSynthesis.getVoices().find((v) => v.lang.startsWith(lang))
      if (voice) utterance.voice = voice

      window.speechSynthesis.speak(utterance)
    },
    [isSupported],
  )

  const stop = useCallback(() => {
    if (isSupported) window.speechSynthesis.cancel()
  }, [isSupported])

  return { isSupported, speak, stop }
}
