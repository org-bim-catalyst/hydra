import { useCallback, useEffect, useRef, useState } from 'react'
import { toWav16kMono } from '../../chat/voice/wavEncoder'
import { tryCreateLevelAnalyser, type LevelAnalyser } from '../audio/audioLevelMeter'
import { useLevelPolling } from './useLevelPolling'

/** Keeps a sample well under the try route's 4 MB limit (16 kHz mono 16-bit ≈ 1.9 MB a minute). */
export const MAX_SAMPLE_SECONDS = 30

/**
 * specs/078 FR-009c — records one short microphone sample and hands it back as the same 16 kHz
 * mono WAV dictation sends, so "Try it" hears exactly what Local Whisper would. Recording stops
 * on its own after {@link MAX_SAMPLE_SECONDS}; the finished sample is then waiting in `stop()`.
 *
 * `start` rejects when the microphone can't be opened, and `stop` when nothing was recorded or
 * the clip can't be converted; the caller shows why.
 *
 * Also exposes which physical device the browser actually opened (`inputDeviceLabel`) and a live
 * 0–1 `inputLevel` while recording, so the administrator can tell a silent/wrong-device recording
 * from a working one before waiting on a transcript.
 */
export function useWavSampleRecorder() {
  const recorderRef = useRef<MediaRecorder | null>(null)
  const chunksRef = useRef<Blob[]>([])
  const stoppedRef = useRef<Promise<Blob> | null>(null)
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const audioContextRef = useRef<AudioContext | null>(null)
  const analyserRef = useRef<LevelAnalyser | null>(null)
  const [isRecording, setIsRecording] = useState(false)
  const [inputDeviceLabel, setInputDeviceLabel] = useState<string | null>(null)

  const release = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current)
      timerRef.current = null
    }
    recorderRef.current?.stream.getTracks().forEach((track) => track.stop())
    recorderRef.current = null
    analyserRef.current?.dispose()
    analyserRef.current = null
    void audioContextRef.current?.close()
    audioContextRef.current = null
    setIsRecording(false)
  }, [])

  useEffect(
    () => () => {
      if (recorderRef.current?.state === 'recording') recorderRef.current.stop()
      release()
    },
    [release],
  )

  const start = useCallback(async () => {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    setInputDeviceLabel(stream.getAudioTracks()[0]?.label || 'Default microphone')

    if (typeof AudioContext !== 'undefined') {
      const context = new AudioContext()
      audioContextRef.current = context
      analyserRef.current = tryCreateLevelAnalyser(context, context.createMediaStreamSource(stream))
    }

    const recorder = new MediaRecorder(stream)
    chunksRef.current = []
    recorder.ondataavailable = (event) => {
      if (event.data.size > 0) chunksRef.current.push(event.data)
    }
    stoppedRef.current = new Promise<Blob>((resolve) => {
      recorder.onstop = () => resolve(new Blob(chunksRef.current, { type: recorder.mimeType }))
    })
    recorderRef.current = recorder
    recorder.start()
    setIsRecording(true)
    timerRef.current = setTimeout(() => {
      if (recorder.state === 'recording') recorder.stop()
    }, MAX_SAMPLE_SECONDS * 1000)
  }, [])

  const inputLevel = useLevelPolling(isRecording, useCallback(() => analyserRef.current?.readLevel() ?? 0, []))

  const stop = useCallback(async (): Promise<Blob> => {
    const recorder = recorderRef.current
    const stopped = stoppedRef.current
    if (!recorder || !stopped) throw new Error('Nothing is being recorded.')
    if (recorder.state === 'recording') recorder.stop()
    const clip = await stopped
    release()
    if (clip.size === 0) throw new Error('Nothing was recorded. Check the microphone and try again.')
    const wav = await toWav16kMono(clip)
    if (!wav.converted) throw new Error("The recording couldn't be converted for Local Whisper. Try again.")
    return wav.blob
  }, [release])

  return { isRecording, start, stop, inputDeviceLabel, inputLevel }
}
