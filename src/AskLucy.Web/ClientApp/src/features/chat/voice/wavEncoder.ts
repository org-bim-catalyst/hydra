import { float32ToInt16Pcm } from './pcm16'

/** specs/078 research D3 — the clip format Local Whisper and OpenAI Whisper both take. */
export const WAV_SAMPLE_RATE = 16000
const WAV_HEADER_BYTES = 44

export interface WavEncodeResult {
  blob: Blob
  /** False when the browser couldn't decode the recording; `blob` is then the original clip,
   * which the server refuses, so the turn moves to the browser built-in. */
  converted: boolean
  error?: unknown
}

/** A 44-byte RIFF header plus 16-bit little-endian samples: PCM, mono, 16 kHz. */
export function encodeWav(samples: Float32Array): Blob {
  const pcm = float32ToInt16Pcm(samples)
  const header = new DataView(new ArrayBuffer(WAV_HEADER_BYTES))
  const writeTag = (offset: number, tag: string) => {
    for (let i = 0; i < tag.length; i++) header.setUint8(offset + i, tag.charCodeAt(i))
  }

  writeTag(0, 'RIFF')
  header.setUint32(4, WAV_HEADER_BYTES - 8 + pcm.byteLength, true)
  writeTag(8, 'WAVE')
  writeTag(12, 'fmt ')
  header.setUint32(16, 16, true)
  header.setUint16(20, 1, true)
  header.setUint16(22, 1, true)
  header.setUint32(24, WAV_SAMPLE_RATE, true)
  header.setUint32(28, WAV_SAMPLE_RATE * 2, true)
  header.setUint16(32, 2, true)
  header.setUint16(34, 16, true)
  writeTag(36, 'data')
  header.setUint32(40, pcm.byteLength, true)

  return new Blob([header.buffer, pcm.buffer as ArrayBuffer], { type: 'audio/wav' })
}

/**
 * Converts a MediaRecorder clip (webm/ogg) to 16 kHz mono PCM WAV in the browser, so the server
 * never runs a media decoder (research D3). Decoding into a 16 kHz context resamples; rendering
 * into a one-channel context downmixes. A decode failure resolves with the original clip rather
 * than throwing: the caller reports it and moves the turn to the browser built-in.
 */
export async function toWav16kMono(clip: Blob): Promise<WavEncodeResult> {
  try {
    const bytes = await clip.arrayBuffer()
    const decoded = await new OfflineAudioContext(1, 1, WAV_SAMPLE_RATE).decodeAudioData(bytes)
    const frames = Math.max(1, Math.ceil(decoded.duration * WAV_SAMPLE_RATE))
    const context = new OfflineAudioContext(1, frames, WAV_SAMPLE_RATE)
    const source = context.createBufferSource()
    source.buffer = decoded
    source.connect(context.destination)
    source.start()
    const rendered = await context.startRendering()
    return { blob: encodeWav(rendered.getChannelData(0)), converted: true }
  } catch (error) {
    return { blob: clip, converted: false, error }
  }
}
