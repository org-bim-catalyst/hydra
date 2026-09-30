import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../../api/httpClient'
import type { ChatStreamEvent } from './aiApi'
import { streamChat, transcribeAudio, transcribeDictationClip } from './aiApi'

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

  // specs/079 contracts/site-boundary-edit-sse-event.md: __SITE_BOUNDARY_EDIT__ trailing SSE event
  it('parses a __SITE_BOUNDARY_EDIT__ trailing event and yields a siteBoundaryEdit event', async () => {
    const editPayload = { chatId: 'chat-1', revision: '0199a0c4-0000-7000-8000-000000000001' }
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        sseResponse([
          'data: The outline editor is open.\n\n',
          `data: __SITE_BOUNDARY_EDIT__${JSON.stringify(editPayload)}\n\n`,
          'data: [DONE]\n\n',
        ]),
      ),
    )

    const events: ChatStreamEvent[] = []
    for await (const event of streamChat('chat-1', [{ role: 'user', content: 'test' }], 'p1', 'm1', undefined)) {
      events.push(event)
    }

    expect(events).toContainEqual({ type: 'siteBoundaryEdit', ...editPayload })
    // The marker is never rendered as prose.
    expect(events.filter((e) => e.type === 'content').some((e) => JSON.stringify(e).includes('SITE_BOUNDARY_EDIT'))).toBe(false)
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

  it('names the spoken language only when the caller knows it', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(new Response(JSON.stringify({ text: 'hi' }), { status: 200 })))
    vi.stubGlobal('fetch', fetchMock)
    const file = new File([new Blob(['audio'])], 'recording.webm', { type: 'audio/webm' })

    await transcribeAudio(file, 'ar')
    await transcribeAudio(file)

    const sentForm = (call: number) => (fetchMock.mock.calls[call] as unknown as [string, RequestInit])[1].body as FormData
    expect(sentForm(0).get('language')).toBe('ar')
    expect(sentForm(1).has('language')).toBe(false)
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

describe('transcribeDictationClip', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  // contracts/dictation-transcription.md — `/ai/voice/transcriptions`, the dictation-only
  // endpoint `useSpeechRecognition`'s clip fallback and `useVoiceRecorder`'s Push-to-Talk post
  // a pre-converted 16 kHz mono WAV to. Always named 'clip.wav', unlike transcribeAudio's
  // browser-mimeType-dependent filename — there is no mimeType left to guess from by this point.
  it('posts the WAV blob as a form field named clip.wav', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(new Response(JSON.stringify({ text: 'hi', language: 'en' }), { status: 200 })))
    vi.stubGlobal('fetch', fetchMock)
    const wav = new Blob(['wav-bytes'], { type: 'audio/wav' })

    await transcribeDictationClip(wav, 'en')

    const sentForm = (fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].body as FormData
    const uploaded = sentForm.get('file') as File
    expect(uploaded.name).toBe('clip.wav')
    expect(sentForm.get('language')).toBe('en')
  })

  it('names the spoken language only when the caller knows it', async () => {
    const fetchMock = vi.fn(() => Promise.resolve(new Response(JSON.stringify({ text: 'hi', language: null }), { status: 200 })))
    vi.stubGlobal('fetch', fetchMock)
    const wav = new Blob(['wav-bytes'], { type: 'audio/wav' })

    await transcribeDictationClip(wav)

    const sentForm = (fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].body as FormData
    expect(sentForm.has('language')).toBe(false)
  })

  it('resolves with the transcribed text and detected language', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(JSON.stringify({ text: 'hello there', language: 'en' }), { status: 200 })),
    )
    const wav = new Blob(['wav-bytes'], { type: 'audio/wav' })

    await expect(transcribeDictationClip(wav)).resolves.toEqual({ text: 'hello there', language: 'en' })
  })

  // contracts/dictation-transcription.md — 503 dictation-engine-unavailable, 422
  // dictation-audio-invalid: both problem-details responses, both carried as an ApiError (via
  // the shared `apiFetch`) so callers (dictationFallback.ts) can tell a broken engine (5xx,
  // `engineUnusable: true`) from a bad recording (4xx) by `status` alone.
  it('throws an ApiError carrying the response status on failure', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(
          JSON.stringify({ title: 'Dictation engine unavailable', status: 503, detail: 'Transcription is not configured.' }),
          { status: 503, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    )
    const wav = new Blob(['wav-bytes'], { type: 'audio/wav' })

    await expect(transcribeDictationClip(wav)).rejects.toMatchObject({
      message: 'Dictation engine unavailable',
      detail: 'Transcription is not configured.',
      status: 503,
    })
  })
})
