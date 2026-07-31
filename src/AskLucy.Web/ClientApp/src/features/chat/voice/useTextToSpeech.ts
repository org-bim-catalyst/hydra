import { useCallback, useRef, useState } from 'react'

const DECAY_PER_SECOND = 3.2 // envelope drops from 1 to 0 in ~0.3s of silence between words

/** Client-side voice output (FR-006) via the browser's native SpeechSynthesis API.
 * Also drives the workspace's 3D sphere (FR-018): `isSpeaking` and `getIntensity()`
 * approximate a real audio envelope from the utterance's own timing events, since
 * `window.speechSynthesis` doesn't expose its audio as an analyzable stream
 * (research.md §3, ADR-0005). `getIntensity` is a ref-based getter rather than React
 * state — matching `VoiceWaveform`'s `getLevels` pattern — because it updates every
 * animation frame while speaking, far past a sane React re-render rate. */
export function useTextToSpeech() {
  const isSupported = typeof window !== 'undefined' && 'speechSynthesis' in window

  const [isSpeaking, setIsSpeaking] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const intensityRef = useRef(0)
  const rafRef = useRef<number | null>(null)
  const lastTickRef = useRef<number | null>(null)
  const isSpeakingRef = useRef(false)

  const stopDecayLoop = useCallback(() => {
    if (rafRef.current !== null) {
      cancelAnimationFrame(rafRef.current)
      rafRef.current = null
    }
    lastTickRef.current = null
  }, [])

  const startDecayLoop = useCallback(() => {
    if (rafRef.current !== null) return
    const tick = (now: number) => {
      const last = lastTickRef.current ?? now
      const dt = (now - last) / 1000
      lastTickRef.current = now
      intensityRef.current = Math.max(0, intensityRef.current - DECAY_PER_SECOND * dt)

      if (intensityRef.current > 0 || isSpeakingRef.current) {
        rafRef.current = requestAnimationFrame(tick)
      } else {
        stopDecayLoop()
      }
    }
    rafRef.current = requestAnimationFrame(tick)
  }, [stopDecayLoop])

  const getIntensity = useCallback(() => intensityRef.current, [])

  const clearError = useCallback(() => setError(null), [])

  const speak = useCallback(
    (text: string, lang: string) => {
      if (!isSupported) {
        setError('Voice output is not supported in this browser.')
        return
      }

      const utterance = new SpeechSynthesisUtterance(text)
      utterance.lang = lang

      const voice = window.speechSynthesis.getVoices().find((v) => v.lang.startsWith(lang))
      if (voice) utterance.voice = voice

      utterance.onstart = () => {
        isSpeakingRef.current = true
        setIsSpeaking(true)
      }
      utterance.onboundary = () => {
        intensityRef.current = 1
        startDecayLoop()
      }
      utterance.onend = () => {
        isSpeakingRef.current = false
        setIsSpeaking(false)
      }
      // constitution §2.VIII: a failed utterance must reach the user, not just fail silently.
      utterance.onerror = () => {
        isSpeakingRef.current = false
        setIsSpeaking(false)
        intensityRef.current = 0
        setError('Voice output failed. Please try again.')
      }

      window.speechSynthesis.speak(utterance)
    },
    [isSupported, startDecayLoop],
  )

  const stop = useCallback(() => {
    if (isSupported) window.speechSynthesis.cancel()
    isSpeakingRef.current = false
    setIsSpeaking(false)
    intensityRef.current = 0
    stopDecayLoop()
  }, [isSupported, stopDecayLoop])

  return { isSupported, speak, stop, isSpeaking, getIntensity, error, clearError }
}
