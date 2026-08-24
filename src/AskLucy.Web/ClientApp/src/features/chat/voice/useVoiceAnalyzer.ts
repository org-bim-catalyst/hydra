import { useCallback, useRef } from 'react'

const FFT_SIZE = 256

/**
 * Owns one shared `AudioContext`/`AnalyserNode` per voice session (research.md Decision 6) —
 * the same audio graph feeds both the sphere's `getReactiveIntensity()` and the speaker
 * output, so there's exactly one decode and one FFT per stream, not one per consumer.
 *
 * Audio arrives as arbitrary byte-chunked, still-compressed (`mp3_44100_128`) bytes from
 * `/api/v1/ai/voice/reply` — a chunk boundary does not line up with an MP3 frame boundary, so
 * a one-shot `decodeAudioData()` per chunk isn't reliable. Instead this uses the Media Source
 * Extensions API (`MediaSource` + `SourceBuffer`) feeding a hidden `<audio>` element, which
 * *is* built for exactly this — progressively appended compressed bytes, played back as they
 * arrive — then taps that element's output via `MediaElementAudioSourceNode` for the
 * analyser/gain graph.
 *
 * Graph: `<audio>` → `MediaElementAudioSourceNode` → `AnalyserNode` → `GainNode` →
 * `destination`. The analyser sits *before* the gain node so muting (FR-021, T067) only
 * suppresses audible output — `getReactiveIntensity()` keeps reacting to the real signal
 * regardless of mute state.
 */
export function useVoiceAnalyzer(onPlaybackError?: (message: string) => void) {
  const audioContextRef = useRef<AudioContext | null>(null)
  const audioElementRef = useRef<HTMLAudioElement | null>(null)
  const mediaSourceRef = useRef<MediaSource | null>(null)
  const sourceBufferRef = useRef<SourceBuffer | null>(null)
  const analyserRef = useRef<AnalyserNode | null>(null)
  const gainRef = useRef<GainNode | null>(null)
  const pendingChunksRef = useRef<Uint8Array<ArrayBuffer>[]>([])
  const frequencyDataRef = useRef<Uint8Array<ArrayBuffer> | null>(null)
  const isMutedRef = useRef(false)
  const hasStartedPlaybackRef = useRef(false)

  const ensureGraph = useCallback(() => {
    if (audioContextRef.current) {
      // If the MediaSource has ended or closed (normal end-of-turn after endStream()) the
      // existing graph cannot receive more data. Tear it down so the block below rebuilds
      // a fresh graph for the next turn. Audio has already drained at this point because
      // endStream() signals no more data — the element plays its buffer then stops.
      const ms = mediaSourceRef.current
      if (ms && ms.readyState === 'open') return
      pendingChunksRef.current = []
      hasStartedPlaybackRef.current = false
      audioElementRef.current?.pause()
      if (audioElementRef.current) audioElementRef.current.removeAttribute('src')
      void audioContextRef.current.close()
      audioContextRef.current = null
      audioElementRef.current = null
      mediaSourceRef.current = null
      sourceBufferRef.current = null
      analyserRef.current = null
      gainRef.current = null
      frequencyDataRef.current = null
    }

    const audioContext = new AudioContext()
    const audioElement = new Audio()
    audioElement.autoplay = true

    // The passive `autoplay` attribute above fails silently if the browser's autoplay
    // policy blocks it — no console error, no rejected promise anywhere in our code, just
    // an element that never makes a sound (constitution §2.VIII: no silent failures). The
    // element's own 'error' event catches that plus mid-stream decode/network failures.
    // play() itself is called later, from the SourceBuffer's first successful append (see
    // below) rather than here: calling it immediately, before any audio bytes exist, made
    // Chromium reject it with MEDIA_ERR_SRC_NOT_SUPPORTED ("no supported source was
    // found") — confirmed in production console logs — since there was nothing playable
    // yet for the browser to evaluate.
    audioElement.addEventListener('error', () => {
      const code = audioElement.error?.code
      onPlaybackError?.(`Audio element reported a playback error (code ${code ?? 'unknown'}).`)
    })

    const mediaSource = new MediaSource()
    audioElement.src = URL.createObjectURL(mediaSource)

    const startPlaybackOnce = () => {
      if (hasStartedPlaybackRef.current) return
      hasStartedPlaybackRef.current = true
      void audioElement.play().catch((err: unknown) => {
        const message = err instanceof Error ? err.message : String(err)
        onPlaybackError?.(`Playback failed to start: ${message}`)
      })
    }

    mediaSource.addEventListener('sourceopen', () => {
      // Guard: in some browsers 'sourceopen' re-fires when readyState transitions from
      // 'ended' back to 'open' (e.g. after a seek on a looping element). Calling
      // addSourceBuffer a second time on the same MediaSource throws QuotaExceededError
      // (the limit is 2 per MediaSource but we only ever need 1). Catch this re-fire
      // so the crash — and the resulting silent voice failure — cannot occur.
      if (mediaSource.sourceBuffers.length > 0) return
      let sourceBuffer: SourceBuffer
      try {
        sourceBuffer = mediaSource.addSourceBuffer('audio/mpeg')
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err)
        onPlaybackError?.(`Failed to initialise audio source buffer: ${message}`)
        return
      }
      sourceBuffer.addEventListener('updateend', () => {
        startPlaybackOnce()
        const next = pendingChunksRef.current.shift()
        if (next && !sourceBuffer.updating) {
          sourceBuffer.appendBuffer(next)
        }
      })
      sourceBufferRef.current = sourceBuffer

      // A chunk may have already arrived (and been queued below) before 'sourceopen'
      // fired — without this, the queue would never drain: draining otherwise only
      // happens inside 'updateend', which never fires without a first appendBuffer call.
      const queued = pendingChunksRef.current.shift()
      if (queued) {
        sourceBuffer.appendBuffer(queued)
      }
    })

    const source = audioContext.createMediaElementSource(audioElement)
    const analyser = audioContext.createAnalyser()
    analyser.fftSize = FFT_SIZE
    const gain = audioContext.createGain()
    gain.gain.value = isMutedRef.current ? 0 : 1

    source.connect(analyser)
    analyser.connect(gain)
    gain.connect(audioContext.destination)

    audioContextRef.current = audioContext
    audioElementRef.current = audioElement
    mediaSourceRef.current = mediaSource
    analyserRef.current = analyser
    gainRef.current = gain
    frequencyDataRef.current = new Uint8Array(new ArrayBuffer(analyser.frequencyBinCount))
  }, [onPlaybackError])

  /** Call for every `audio-chunk` event, in arrival order (contracts/voice-reply-stream.md). */
  const playAudioChunk = useCallback(
    (base64Audio: string) => {
      ensureGraph()
      const binary = atob(base64Audio)
      const bytes = new Uint8Array(new ArrayBuffer(binary.length))
      for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)

      const sourceBuffer = sourceBufferRef.current
      if (sourceBuffer && !sourceBuffer.updating && pendingChunksRef.current.length === 0) {
        sourceBuffer.appendBuffer(bytes)
      } else {
        pendingChunksRef.current.push(bytes)
      }
    },
    [ensureGraph],
  )

  /** Ends the MediaSource stream once the reply's `done`/`audio-failed` event arrives — no
   * more chunks will be appended. */
  const endStream = useCallback(() => {
    const mediaSource = mediaSourceRef.current
    if (mediaSource && mediaSource.readyState === 'open' && !sourceBufferRef.current?.updating) {
      try {
        mediaSource.endOfStream()
      } catch {
        // Already ended/closed — nothing to do.
      }
    }
  }, [])

  /** Ref-based getter (not React state) — read every animation frame by `ReactiveSphere.tsx`
   * via `ChatPage.tsx`, matching `useTextToSpeech.getIntensity()`'s existing signature
   * (research.md Decision 6). Computed from the *analyser*, upstream of the mute gain node,
   * so muting never affects visualization (FR-021). */
  const getReactiveIntensity = useCallback((): number => {
    const analyser = analyserRef.current
    const data = frequencyDataRef.current
    if (!analyser || !data) return 0

    analyser.getByteFrequencyData(data)
    let sum = 0
    for (let i = 0; i < data.length; i++) sum += data[i]
    return Math.min(1, sum / data.length / 255)
  }, [])

  const setMuted = useCallback((muted: boolean) => {
    isMutedRef.current = muted
    if (gainRef.current) {
      gainRef.current.gain.value = muted ? 0 : 1
    }
  }, [])

  /** FR-023: stop clears the queue and tears down the graph so the next turn starts fresh. */
  const reset = useCallback(() => {
    pendingChunksRef.current = []
    hasStartedPlaybackRef.current = false
    audioElementRef.current?.pause()
    if (audioElementRef.current) {
      audioElementRef.current.removeAttribute('src')
    }
    void audioContextRef.current?.close()
    audioContextRef.current = null
    audioElementRef.current = null
    mediaSourceRef.current = null
    sourceBufferRef.current = null
    analyserRef.current = null
    gainRef.current = null
    frequencyDataRef.current = null
  }, [])

  return { playAudioChunk, endStream, getReactiveIntensity, setMuted, reset }
}
