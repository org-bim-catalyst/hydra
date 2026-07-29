import { useCallback, useRef, useState } from 'react'

const TARGET_SAMPLE_RATE = 16000
const LEVEL_HISTORY_SIZE = 64

/** Linear-interpolation downsample to 16kHz — whisper.cpp's expected input rate. */
function downsample(samples: Float32Array, inputSampleRate: number): Float32Array {
  if (inputSampleRate === TARGET_SAMPLE_RATE) {
    return samples
  }

  const ratio = inputSampleRate / TARGET_SAMPLE_RATE
  const outputLength = Math.round(samples.length / ratio)
  const output = new Float32Array(outputLength)

  for (let i = 0; i < outputLength; i++) {
    const position = i * ratio
    const lower = Math.floor(position)
    const upper = Math.min(lower + 1, samples.length - 1)
    const weight = position - lower
    output[i] = samples[lower] * (1 - weight) + samples[upper] * weight
  }

  return output
}

/** Builds a 16-bit PCM mono WAV file — the one format Whisper.net's WaveParser reads directly. */
function encodeWav(samples: Float32Array, sampleRate: number): Blob {
  const buffer = new ArrayBuffer(44 + samples.length * 2)
  const view = new DataView(buffer)

  const writeString = (offset: number, text: string) => {
    for (let i = 0; i < text.length; i++) view.setUint8(offset + i, text.charCodeAt(i))
  }

  writeString(0, 'RIFF')
  view.setUint32(4, 36 + samples.length * 2, true)
  writeString(8, 'WAVE')
  writeString(12, 'fmt ')
  view.setUint32(16, 16, true)
  view.setUint16(20, 1, true) // PCM
  view.setUint16(22, 1, true) // mono
  view.setUint32(24, sampleRate, true)
  view.setUint32(28, sampleRate * 2, true) // byte rate
  view.setUint16(32, 2, true) // block align
  view.setUint16(34, 16, true) // bits per sample
  writeString(36, 'data')
  view.setUint32(40, samples.length * 2, true)

  let offset = 44
  for (const sample of samples) {
    const clamped = Math.max(-1, Math.min(1, sample))
    view.setInt16(offset, clamped * 0x7fff, true)
    offset += 2
  }

  return new Blob([buffer], { type: 'audio/wav' })
}

/**
 * Records the microphone via an AudioWorkletNode (the non-deprecated replacement for
 * ScriptProcessorNode) and hands back a WAV blob — deliberately not MediaRecorder (which
 * only produces webm/opus, not the WAV Whisper.net needs) and not the browser's native
 * SpeechRecognition (whose "network" errors on non-Chrome browsers turned out to be
 * Google's speech backend rejecting non-Chrome clients outright).
 *
 * Also exposes `getLevels()` — a ring buffer of recent peak amplitudes, read imperatively
 * by the waveform visualizer's own animation-frame loop rather than via React state, so a
 * ~344Hz stream of audio blocks never drives a React re-render.
 */
export function useWavRecorder() {
  const [isRecording, setIsRecording] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const audioContextRef = useRef<AudioContext | null>(null)
  const workletNodeRef = useRef<AudioWorkletNode | null>(null)
  const sourceRef = useRef<MediaStreamAudioSourceNode | null>(null)
  const streamRef = useRef<MediaStream | null>(null)
  const chunksRef = useRef<Float32Array[]>([])
  const levelsRef = useRef<Float32Array>(new Float32Array(LEVEL_HISTORY_SIZE))
  const levelWriteIndexRef = useRef(0)

  const isSupported =
    typeof navigator !== 'undefined' &&
    !!navigator.mediaDevices?.getUserMedia &&
    typeof AudioContext !== 'undefined' &&
    typeof AudioWorkletNode !== 'undefined'

  const cleanup = () => {
    workletNodeRef.current?.port.close()
    workletNodeRef.current?.disconnect()
    workletNodeRef.current = null
    sourceRef.current?.disconnect()
    sourceRef.current = null
    streamRef.current?.getTracks().forEach((track) => track.stop())
    streamRef.current = null
    void audioContextRef.current?.close()
    audioContextRef.current = null
  }

  const start = useCallback(async () => {
    setError(null)
    chunksRef.current = []
    levelsRef.current.fill(0)
    levelWriteIndexRef.current = 0

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
      streamRef.current = stream

      const audioContext = new AudioContext()
      audioContextRef.current = audioContext
      await audioContext.audioWorklet.addModule('/audio/recorder-worklet.js')

      const source = audioContext.createMediaStreamSource(stream)
      sourceRef.current = source

      const workletNode = new AudioWorkletNode(audioContext, 'recorder-worklet')
      workletNodeRef.current = workletNode

      workletNode.port.onmessage = (event: MessageEvent<Float32Array>) => {
        const chunk = event.data
        chunksRef.current.push(chunk)

        let peak = 0
        for (const sample of chunk) {
          const abs = Math.abs(sample)
          if (abs > peak) peak = abs
        }
        levelsRef.current[levelWriteIndexRef.current] = peak
        levelWriteIndexRef.current = (levelWriteIndexRef.current + 1) % LEVEL_HISTORY_SIZE
      }

      source.connect(workletNode)
      setIsRecording(true)
    } catch {
      setError('Microphone access was denied. Check your browser’s site permissions and try again.')
      cleanup()
    }
  }, [])

  /** Levels in chronological order (oldest first), for a waveform that scrolls left-to-right. */
  const getLevels = useCallback((): Float32Array => {
    const ordered = new Float32Array(LEVEL_HISTORY_SIZE)
    const start = levelWriteIndexRef.current
    for (let i = 0; i < LEVEL_HISTORY_SIZE; i++) {
      ordered[i] = levelsRef.current[(start + i) % LEVEL_HISTORY_SIZE]
    }
    return ordered
  }, [])

  const stop = useCallback((): Blob | null => {
    if (!audioContextRef.current) {
      return null
    }

    const sampleRate = audioContextRef.current.sampleRate
    const totalLength = chunksRef.current.reduce((sum, chunk) => sum + chunk.length, 0)
    const merged = new Float32Array(totalLength)
    let offset = 0
    for (const chunk of chunksRef.current) {
      merged.set(chunk, offset)
      offset += chunk.length
    }

    cleanup()
    setIsRecording(false)

    if (totalLength === 0) {
      return null
    }

    return encodeWav(downsample(merged, sampleRate), TARGET_SAMPLE_RATE)
  }, [])

  const discard = useCallback(() => {
    cleanup()
    setIsRecording(false)
    chunksRef.current = []
  }, [])

  const clearError = useCallback(() => setError(null), [])

  return { isSupported, isRecording, error, start, stop, discard, getLevels, clearError, setError }
}
