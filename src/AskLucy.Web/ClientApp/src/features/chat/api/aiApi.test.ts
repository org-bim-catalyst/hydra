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
      confidenceLevel: 'high',
      confidenceReason: 'The map service matched this exact spot.',
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

  // The site-boundary confirmation reports a second action, finishing seconds after the location
  // did. Appending it to the reply ran two unrelated sentences together and rewrote a bubble the
  // user had already read, so the server breaks the message instead.
  it('parses a __MESSAGE_BREAK__ event and yields a messageBreak', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        sseResponse([
          'data: Centred the viewer on it.\n\n',
          'data: __MESSAGE_BREAK__\n\n',
          "data: I've outlined the site boundary.\n\n",
          'data: [DONE]\n\n',
        ]),
      ),
    )

    const events: ChatStreamEvent[] = []
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)) {
      events.push(event)
    }

    expect(events).toEqual([
      { type: 'content', delta: 'Centred the viewer on it.' },
      { type: 'messageBreak', pendingLabel: null },
      { type: 'content', delta: "I've outlined the site boundary." },
    ])
  })

  // The break is announced before the work that fills the new message, so it can say what that
  // work is — otherwise the reply looks finished and the user waits in silence.
  it('carries the pending label when the break announces work that has not finished', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        sseResponse([
          'data: Centred the viewer on it.\n\n',
          'data: __MESSAGE_BREAK__{"pendingLabel":"Finding the site boundary"}\n\n',
          'data: [DONE]\n\n',
        ]),
      ),
    )

    const events: ChatStreamEvent[] = []
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)) {
      events.push(event)
    }

    expect(events).toContainEqual({ type: 'messageBreak', pendingLabel: 'Finding the site boundary' })
  })

  it('treats a line that merely starts with the marker as content, since the marker carries no payload', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(sseResponse(['data: __MESSAGE_BREAK__ is the marker\n\n', 'data: [DONE]\n\n'])),
    )

    const events: ChatStreamEvent[] = []
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)) {
      events.push(event)
    }

    expect(events).toEqual([{ type: 'content', delta: '__MESSAGE_BREAK__ is the marker' }])
  })

  // specs/045-conversational-agent-runtime FR-021 — the offer closing a turn, last before [DONE].
  it('parses a __ACTIONS__ trailing event and yields an actions event', async () => {
    const actionsPayload = {
      offeredByMessageId: 'msg-1',
      question: 'What would you like to do next?',
      actions: [
        {
          kind: 'capability',
          capabilityKey: 'search_knowledge_base',
          text: null,
          label: 'Search my knowledge bases',
          description: 'Look for this site in your attached documents.',
          arguments: { query: 'Al Safa Park 2' },
          isDecline: false,
        },
        {
          kind: 'decline',
          capabilityKey: null,
          text: null,
          label: 'Nothing for now',
          description: '',
          arguments: null,
          isDecline: true,
        },
      ],
    }
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        sseResponse([
          'data: Yes — it is a public park.\n\n',
          `data: __ACTIONS__${JSON.stringify(actionsPayload)}\n\n`,
          'data: [DONE]\n\n',
        ]),
      ),
    )

    const events: ChatStreamEvent[] = []
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)) {
      events.push(event)
    }

    expect(events).toHaveLength(2)
    expect(events[1]).toEqual({ type: 'actions', ...actionsPayload })
  })

  it('defaults the question to an empty string when the offer omits it', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        sseResponse([
          'data: __ACTIONS__{"offeredByMessageId":"msg-1","question":null,"actions":[]}\n\n',
          'data: [DONE]\n\n',
        ]),
      ),
    )

    const events: ChatStreamEvent[] = []
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)) {
      events.push(event)
    }

    expect(events).toEqual([{ type: 'actions', offeredByMessageId: 'msg-1', question: '', actions: [] }])
  })

  // specs/042-site-boundary-resolution T031: __SITE_BOUNDARY__ trailing SSE event
  it('parses a __SITE_BOUNDARY__ trailing event and yields a siteBoundary event', async () => {
    const boundaryPayload = {
      siteName: 'Al Safa Park 2',
      centroid: { latitude: 25.156, longitude: 55.2218 },
      polygon: [
        { latitude: 25.156, longitude: 55.221 },
        { latitude: 25.156, longitude: 55.222 },
        { latitude: 25.155, longitude: 55.222 },
      ],
      areaSquareMeters: 15000,
      confidence: 0.92,
      confidenceLevel: 'high',
      source: 'OsmBoundary',
      sourceDetail: 'OpenStreetMap (leisure=park)',
      alternativeCandidateNames: [],
    }
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        sseResponse([
          'data: Here you go.\n\n',
          `data: __SITE_BOUNDARY__${JSON.stringify(boundaryPayload)}\n\n`,
          'data: [DONE]\n\n',
        ]),
      ),
    )

    const events: ChatStreamEvent[] = []
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)) {
      events.push(event)
    }

    expect(events).toHaveLength(2)
    // A payload from before specs/077 carries no separate buildings, and reads as having none.
    expect(events[1]).toEqual({ type: 'siteBoundary', ...boundaryPayload, additionalPolygons: [] })
  })

  // specs/052-solar-analysis research D3: __SOLAR_ANALYSIS__ trailing SSE event
  it('parses a __SOLAR_ANALYSIS__ trailing event and yields a solarAnalysis event', async () => {
    const solarPayload = { date: '2026-09-13', timeOfDay: '14:00' }
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        sseResponse([
          'data: Opening the sun and shadow analysis.\n\n',
          `data: __SOLAR_ANALYSIS__${JSON.stringify(solarPayload)}\n\n`,
          'data: [DONE]\n\n',
        ]),
      ),
    )

    const events: ChatStreamEvent[] = []
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)) {
      events.push(event)
    }

    expect(events).toContainEqual({ type: 'solarAnalysis', ...solarPayload })
  })

  // specs/060: streamChat bypasses apiFetch (raw fetch, for SSE), so a revoked session used to
  // surface only a generic "chat request failed" error instead of the sign-out/login flow.
  it('retries once after a silent refresh when the chat request itself 401s', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Authentication required' }), { status: 401 }))
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ userId: 'user-1', accessToken: 'new-token' }), { status: 200 }),
      )
      .mockResolvedValueOnce(sseResponse(['data: Hello!\n\n', 'data: [DONE]\n\n']))
    vi.stubGlobal('fetch', fetchMock)

    const events: ChatStreamEvent[] = []
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)) {
      events.push(event)
    }

    expect(events).toEqual([{ type: 'content', delta: 'Hello!' }])
    expect(fetchMock).toHaveBeenCalledTimes(3)
    expect(fetchMock.mock.calls[1][0]).toContain('/auth/refresh')
  })

  it('redirects to /login and never settles when refresh also fails, instead of yielding a generic error', async () => {
    const assignSpy = vi.fn()
    Object.defineProperty(window, 'location', { configurable: true, value: { ...window.location, assign: assignSpy } })
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Authentication required' }), { status: 401 }))
        .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'No refresh token present' }), { status: 401 })),
    )

    const settled = vi.fn()
    void streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)
      .next()
      .then(settled, settled)

    await vi.waitFor(() => {
      expect(assignSpy).toHaveBeenCalledWith('/login')
    })
    expect(settled).not.toHaveBeenCalled()
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

  it('retries once after a silent refresh when the transcription request itself 401s', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Authentication required' }), { status: 401 }))
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ userId: 'user-1', accessToken: 'new-token' }), { status: 200 }),
      )
      .mockResolvedValueOnce(new Response(JSON.stringify({ text: 'hello world' }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    const file = new File([new Blob(['audio'])], 'recording.webm', { type: 'audio/webm' })

    await expect(transcribeAudio(file)).resolves.toBe('hello world')
    expect(fetchMock).toHaveBeenCalledTimes(3)
  })

  it('redirects to /login and never settles when refresh also fails, instead of throwing', async () => {
    const assignSpy = vi.fn()
    Object.defineProperty(window, 'location', { configurable: true, value: { ...window.location, assign: assignSpy } })
    vi.stubGlobal(
      'fetch',
      vi
        .fn()
        .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Authentication required' }), { status: 401 }))
        .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'No refresh token present' }), { status: 401 })),
    )

    const file = new File([new Blob(['audio'])], 'recording.webm', { type: 'audio/webm' })
    const settled = vi.fn()
    void transcribeAudio(file).then(settled, settled)

    await vi.waitFor(() => {
      expect(assignSpy).toHaveBeenCalledWith('/login')
    })
    expect(settled).not.toHaveBeenCalled()
  })
})
