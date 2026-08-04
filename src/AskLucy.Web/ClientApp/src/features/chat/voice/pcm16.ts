const TARGET_SAMPLE_RATE = 16000

/** Linear-interpolation downsample to 16kHz — the sample rate ElevenLabs' realtime STT
 * (`useSpeechRecognition.ts`) expects. Extracted here in case a future capture path needs
 * the same resampling math. */
export function downsampleTo16kHz(samples: Float32Array, inputSampleRate: number): Float32Array {
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

/** 16-bit signed PCM, little-endian — the raw sample format ElevenLabs' realtime STT expects
 * per-message (`audio_base_64` on each `input_audio_chunk`, verified SPEC-013 T010). */
export function float32ToInt16Pcm(samples: Float32Array): Int16Array {
  const output = new Int16Array(samples.length)
  for (let i = 0; i < samples.length; i++) {
    const clamped = Math.max(-1, Math.min(1, samples[i]))
    output[i] = clamped * 0x7fff
  }
  return output
}

/** Base64-encodes a typed array's raw bytes for JSON-message transport over the realtime STT
 * WebSocket (research.md Decision 2). */
export function toBase64(bytes: Int16Array): string {
  const byteView = new Uint8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength)
  let binary = ''
  for (let i = 0; i < byteView.length; i++) binary += String.fromCharCode(byteView[i])
  return btoa(binary)
}
