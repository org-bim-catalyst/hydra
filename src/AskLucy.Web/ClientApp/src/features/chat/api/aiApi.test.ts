import { afterEach, describe, expect, it, vi } from 'vitest'
import { streamChat } from './aiApi'

function sseResponse(lines: string[]): Response {
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      const encoder = new TextEncoder()
      for (const line of lines) {
        controller.enqueue(encoder.encode(line))
      }
      controller.close()
    },
  })
  return new Response(body, { status: 200 })
}

describe('streamChat', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('preserves the single meaningful space each streamed chunk carries', async () => {
    // Mirrors AiController.cs writing `data: {chunk}\n\n` — most word tokens from OpenAI
    // arrive with their own leading space (" I", " can", " hear"), which is the word boundary.
    // A regression here (a full .trim() instead of stripping only the protocol space) ran
    // every word together with no spaces at all.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        sseResponse([
          'data: Yes,\n\n',
          'data:  I\n\n',
          'data:  can\n\n',
          'data:  hear\n\n',
          'data:  you.\n\n',
          'data: [DONE]\n\n',
        ]),
      ),
    )

    const chunks: string[] = []
    for await (const chunk of streamChat('chat-1', [{ role: 'user', content: 'Hello' }], 'provider-1', 'model-1', undefined)) {
      chunks.push(chunk)
    }

    expect(chunks.join('')).toBe('Yes, I can hear you.')
  })
})
