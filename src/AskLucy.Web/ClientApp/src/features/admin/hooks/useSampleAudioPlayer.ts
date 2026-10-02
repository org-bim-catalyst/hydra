import { useCallback, useEffect, useRef, useState } from 'react'
import { tryCreateLevelAnalyser, type LevelAnalyser } from '../audio/audioLevelMeter'
import { useLevelPolling } from './useLevelPolling'

/**
 * specs/070 — plays one base64 audio sample at a time. Starting a sample stops the previous one,
 * and unmounting stops whatever is playing and releases its object URL.
 *
 * `play` never rejects: a sample the browser refuses to play lands in `error` for the page to show.
 *
 * Also exposes the system's default audio output device (`outputDeviceLabel`) and a live 0–1
 * `outputLevel` while playing, so a silent/misrouted speaker shows up before the administrator
 * assumes the voice itself is broken.
 */
export function useSampleAudioPlayer() {
  const audioRef = useRef<HTMLAudioElement | null>(null)
  const urlRef = useRef<string | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const analyserRef = useRef<LevelAnalyser | null>(null)
  const [isPlaying, setIsPlaying] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [outputDeviceLabel, setOutputDeviceLabel] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    navigator.mediaDevices
      ?.enumerateDevices?.()
      .then((devices) => {
        if (cancelled) return
        const outputs = devices.filter((d) => d.kind === 'audiooutput')
        const label = (outputs.find((d) => d.deviceId === 'default') ?? outputs[0])?.label
        setOutputDeviceLabel(label || null)
      })
      .catch(() => setOutputDeviceLabel(null))
    return () => {
      cancelled = true
    }
  }, [])

  const stop = useCallback(() => {
    audioRef.current?.pause()
    audioRef.current = null
    if (urlRef.current) {
      URL.revokeObjectURL(urlRef.current)
      urlRef.current = null
    }
    analyserRef.current?.dispose()
    analyserRef.current = null
    setIsPlaying(false)
  }, [])

  useEffect(
    () => () => {
      stop()
      void audioContextRef.current?.close()
      audioContextRef.current = null
    },
    [stop],
  )

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
        return
      }

      // Wrapping the element in a MediaElementAudioSourceNode disconnects its native output path,
      // so it must be reconnected to `destination` here or playback goes silent.
      if (typeof AudioContext !== 'undefined' && audioRef.current === audio) {
        try {
          const context = audioContextRef.current ?? new AudioContext()
          audioContextRef.current = context
          const source = context.createMediaElementSource(audio)
          source.connect(context.destination)
          analyserRef.current = tryCreateLevelAnalyser(context, source)
        } catch {
          analyserRef.current = null
        }
      }
    },
    [stop],
  )

  const outputLevel = useLevelPolling(
    isPlaying,
    useCallback(() => analyserRef.current?.readLevel() ?? 0, []),
  )

  const clearError = useCallback(() => setError(null), [])

  return { play, stop, isPlaying, error, clearError, outputDeviceLabel, outputLevel }
}
