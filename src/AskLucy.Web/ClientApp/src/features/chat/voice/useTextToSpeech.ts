import { useCallback, useEffect, useRef, useState } from 'react'
import { selectPersonaVoice } from './selectPersonaVoice'

const DECAY_PER_SECOND = 3.2 // envelope drops from 1 to 0 in ~0.3s of silence between words

/** Client-side voice output (FR-006) via the browser's native SpeechSynthesis API,
 * with a curated/heuristic persona-matching voice (spec 010-lucy-brand-refresh FR-001–005,
 * contracts/voice-persona-mapping.md) so playback sounds like the same young-adult female
 * persona across browsers/languages instead of an arbitrary per-platform default — closes
 * the gap ADR-0005 deferred.
 * Also drives the workspace's 3D sphere (FR-018): `isSpeaking` and `getIntensity()`
 * approximate a real audio envelope from the utterance's own timing events, since
 * `window.speechSynthesis` doesn't expose its audio as an analyzable stream
 * (research.md §3, ADR-0005). `getIntensity` is a ref-based getter rather than React
 * state because it updates every animation frame while speaking, far past a sane React
 * re-render rate. */
export function useTextToSpeech() {
  const isSupported = typeof window !== 'undefined' && 'speechSynthesis' in window

  const [isSpeaking, setIsSpeaking] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const intensityRef = useRef(0)
  const rafRef = useRef<number | null>(null)
  const lastTickRef = useRef<number | null>(null)
  const isSpeakingRef = useRef(false)
  // Chromium/Edge populate speechSynthesis.getVoices() asynchronously — the first call
  // after page load routinely returns [] before the browser has finished enumerating.
  // 'voiceschanged' is supposed to fire once it's ready, but doesn't always do so
  // reliably, and enumeration can take longer than the gap between mount and the first
  // spoken reply. Priming here (mount time) and polling as a backup until the list is
  // populated means speak() itself can stay synchronous instead of needing to wait
  // mid-utterance.
  const voicesRef = useRef<SpeechSynthesisVoice[]>([])

  useEffect(() => {
    if (typeof window === 'undefined' || !('speechSynthesis' in window)) return

    const synth = window.speechSynthesis
    const updateVoices = () => {
      voicesRef.current = synth.getVoices()
    }
    updateVoices()
    synth.addEventListener('voiceschanged', updateVoices)

    let pollId: ReturnType<typeof setInterval> | null = null
    let pollTimeoutId: ReturnType<typeof setTimeout> | null = null
    if (voicesRef.current.length === 0) {
      pollId = setInterval(() => {
        const voices = synth.getVoices()
        if (voices.length > 0) {
          voicesRef.current = voices
          if (pollId !== null) clearInterval(pollId)
        }
      }, 200)
      pollTimeoutId = setTimeout(() => {
        if (pollId !== null) clearInterval(pollId)
      }, 5000)
    }

    return () => {
      synth.removeEventListener('voiceschanged', updateVoices)
      if (pollId !== null) clearInterval(pollId)
      if (pollTimeoutId !== null) clearTimeout(pollTimeoutId)
    }
  }, [])

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

      // FR-001–005/contracts/voice-persona-mapping.md: a curated or heuristically-scored
      // persona-matching voice, never the browser's own arbitrary per-language default.
      // Prefer the primed/cached list (see the mount effect above); falling back to a
      // direct call covers the rare case where speak() fires before that effect has run.
      const voices =
        voicesRef.current.length > 0 ? voicesRef.current : window.speechSynthesis.getVoices()
      const { voice } = selectPersonaVoice(lang, voices)
      if (!voice) {
        // constitution §2.VIII / FR-005: no voice for this language at all is a visible
        // failure, never a silent speak() with an unset (browser-arbitrary) voice.
        console.error(
          `Voice output: no voice matched language "${lang}" among ${voices.length} available voices.`,
        )
        setError('Voice output failed. Please try again.')
        return
      }

      const utterance = new SpeechSynthesisUtterance(text)
      utterance.lang = lang
      utterance.voice = voice

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
      utterance.onerror = (event) => {
        console.error(`Voice output: SpeechSynthesisUtterance error "${event.error}".`)
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
