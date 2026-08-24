import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../api/httpClient'
import type { ChatStreamEvent } from './aiApi'
import { streamChat, transcribeAudio } from './aiApi'

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

  // T014 — specs/036-startup-geolocation US3: __LOCATION__ trailing SSE event
  it('parses a __LOCATION__ trailing event and yields a location event (US3, FR-013)', async () => {
    const locationPayload = {
      latitude: 25.2048,
      longitude: 55.2708,
      locationName: 'Al Safa 2 Park',
      confidence: 0.97,
      source: 'agent',
      locationType: null,
      viewport: null,
    }
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        sseResponse([
          'data: Hello!\n\n',
          `data: __LOCATION__${JSON.stringify(locationPayload)}\n\n`,
          'data: [DONE]\n\n',
        ]),
      ),
    )

    const events: ChatStreamEvent[] = []
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)) {
      events.push(event)
    }

    expect(events).toHaveLength(2)
    expect(events[0]).toEqual({ type: 'content', delta: 'Hello!' })
    expect(events[1]).toEqual({ type: 'location', ...locationPayload })
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
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'Hello' }], 'provider-1', 'model-1', undefined)) {
      if (event.type === 'content') chunks.push(event.delta)
    }

    expect(chunks.join('')).toBe('Yes, I can hear you.')
  })
})

describe('transcribeAudio', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  // specs/032 T005: before this fix, a rejected recording surfaced only
  // "Transcription failed with 400" — the Problem Details body's `detail` was discarded
  // entirely. This proves the real detail now reaches the caller.
  it('throws an ApiError carrying the Problem Details detail, not a bare status code', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({
            type: 'https://hydra.bimcatalyst.com/problems/ai-provider-request-invalid',
            title: 'AI provider rejected the request',
            status: 400,
            detail: 'The AI provider could not process this request. Please try again.',
          }),
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )

    const file = new File([new Blob(['audio'])], 'recording.webm', { type: 'audio/webm' })

    await expect(transcribeAudio(file)).rejects.toMatchObject({
      message: 'The AI provider could not process this request. Please try again.',
      status: 400,
    })
  })

  it('throws an ApiError instance', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'Bad Request', status: 400 }), { status: 400 })),
    )

    const file = new File([new Blob(['audio'])], 'recording.webm', { type: 'audio/webm' })

    await expect(transcribeAudio(file)).rejects.toBeInstanceOf(ApiError)
  })

  it('falls back to a generic message when the response body is not valid JSON', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('not json', { status: 500 })))

    const file = new File([new Blob(['audio'])], 'recording.webm', { type: 'audio/webm' })

    await expect(transcribeAudio(file)).rejects.toMatchObject({ message: 'Transcription failed', status: 500 })
  })
})
