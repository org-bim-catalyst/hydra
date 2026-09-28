import { afterEach, describe, expect, it, vi } from 'vitest'
import { encodeWav, toWav16kMono } from './wavEncoder'

async function bytesOf(blob: Blob): Promise<DataView> {
  // jsdom's Blob isn't the one Node's Response reads, so go through FileReader.
  const buffer = await new Promise<ArrayBuffer>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as ArrayBuffer)
    reader.onerror = () => reject(reader.error)
    reader.readAsArrayBuffer(blob)
  })
  return new DataView(buffer)
}

function tag(view: DataView, offset: number): string {
  return String.fromCharCode(...Array.from({ length: 4 }, (_, i) => view.getUint8(offset + i)))
}

function stubOfflineAudioContext(decode: () => Promise<unknown>, rendered: Float32Array) {
  class FakeOfflineAudioContext {
    destination = {}
    decodeAudioData = decode
    createBufferSource() {
      return { buffer: null, connect: vi.fn(), start: vi.fn() }
    }
    startRendering() {
      return Promise.resolve({ getChannelData: () => rendered })
    }
  }
  vi.stubGlobal('OfflineAudioContext', FakeOfflineAudioContext)
}

describe('wavEncoder (specs/078 research D3)', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('writes a 44-byte RIFF header for 16 kHz mono 16-bit PCM', async () => {
    const view = await bytesOf(encodeWav(new Float32Array([0, 0.5, -1, 1])))

    expect(view.byteLength).toBe(44 + 8)
    expect(tag(view, 0)).toBe('RIFF')
    expect(view.getUint32(4, true)).toBe(36 + 8)
    expect(tag(view, 8)).toBe('WAVE')
    expect(tag(view, 12)).toBe('fmt ')
    expect(view.getUint16(20, true)).toBe(1)
    expect(view.getUint16(22, true)).toBe(1)
    expect(view.getUint32(24, true)).toBe(16000)
    expect(view.getUint16(34, true)).toBe(16)
    expect(tag(view, 36)).toBe('data')
    expect(view.getUint32(40, true)).toBe(8)
    expect(view.getInt16(44 + 4, true)).toBe(-0x7fff)
    expect(view.getInt16(44 + 6, true)).toBe(0x7fff)
  })

  it('converts a decodable recording to WAV', async () => {
    stubOfflineAudioContext(() => Promise.resolve({ duration: 0.25 }), new Float32Array(4000))

    const result = await toWav16kMono(new Blob([new Uint8Array([1, 2, 3])], { type: 'audio/webm' }))

    expect(result.converted).toBe(true)
    expect(result.blob.type).toBe('audio/wav')
    expect((await bytesOf(result.blob)).getUint32(40, true)).toBe(8000)
  })

  it('resolves to the raw clip with converted: false when decoding fails', async () => {
    const failure = new DOMException('Unable to decode audio data', 'EncodingError')
    stubOfflineAudioContext(() => Promise.reject(failure), new Float32Array())
    const clip = new Blob([new Uint8Array([1, 2, 3])], { type: 'audio/webm' })

    const result = await toWav16kMono(clip)

    expect(result).toEqual({ blob: clip, converted: false, error: failure })
  })
})
