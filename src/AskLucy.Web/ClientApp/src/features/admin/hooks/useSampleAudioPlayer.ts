import { useCallback, useEffect, useRef, useState } from 'react'

/**
 * specs/070 — plays one base64 audio sample at a time. Starting a sample stops the previous one,
 * and unmounting stops whatever is playing and releases its object URL.
 *
 * `play` never rejects: a sample the browser refuses to play lands in `error` for the page to show.
 */
export function useSampleAudioPlayer() {
  const audioRef = useRef<HTMLAudioElement | null>(null)
  const urlRef = useRef<string | null>(null)
  const [isPlaying, setIsPlaying] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const stop = useCallback(() => {
    audioRef.current?.pause()
    audioRef.current = null
    if (urlRef.current) {
      URL.revokeObjectURL(urlRef.current)
      urlRef.current = null
    }
    setIsPlaying(false)
  }, [])

  useEffect(() => stop, [stop])

  const play = useCallback(
    async (audioBase64: string, contentType: string) => {
      stop()
      setError(null)

      const fail = (audio: HTMLAudioElement) => {
        // A sample that was stopped or replaced is not a failure of the current one.
        if (audioRef.current !== audio) return
        stop()
        setError('Your browser could not play this sample.')
      }

      const bytes = Uint8Array.from(atob(audioBase64), (c) => c.charCodeAt(0))
      const url = URL.createObjectURL(new Blob([bytes], { type: contentType }))
      const audio = new Audio(url)
      audioRef.current = audio
      urlRef.current = url
      audio.onended = () => {
        if (audioRef.current === audio) stop()
      }
      audio.onerror = () => fail(audio)

      setIsPlaying(true)
      try {
        await audio.play()
      } catch {
        fail(audio)
      }
    },
    [stop],
  )

  const clearError = useCallback(() => setError(null), [])

  return { play, stop, isPlaying, error, clearError }
}
