import { apiFetch, ApiError, attemptSilentRefresh, redirectToLogin } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

/** The trailing RAG-specific fields (specs/016-rag-semantic-search US1, research.md Decision 9) are undefined for a plain, non-RAG citation. */
export interface Citation {
  id: string
  sourceLabel: string
  sourceReference: string | null
  documentChunkId?: string | null
  knowledgeBaseId?: string | null
  documentId?: string | null
  documentVersionId?: string | null
  pageNumber?: number | null
  section?: string | null
  /** The retrieved passage text — only present on citations captured live from a just-streamed reply (not yet re-fetched from persisted history). */
  excerpt?: string | null
}

export type RagRetrievalOutcome = 'Grounded' | 'NoRelevantContent' | 'Unavailable'

/** specs/018-ai-memory-system, research.md Decision 3. */
export type MemoryRetrievalOutcome = 'Found' | 'NoneRelevant' | 'Unavailable'

/**
 * specs/045-conversational-agent-runtime FR-021a — one row of an offer. `capabilityKey` is set
 * for `capability`/`flowVariant` rows and null for `followUp`/`decline`; `text` is the composed
 * instruction for a `followUp` row only. Selecting a row never invokes a capability directly —
 * dispatch (specs/045 Phase 5) is what turns a selection into a request; this shape only
 * describes what the card renders and echoes back.
 */
export interface SuggestedAction {
  kind: 'flowVariant' | 'capability' | 'followUp' | 'decline'
  capabilityKey: string | null
  text: string | null
  label: string
  description: string
  arguments: unknown
  isDecline: boolean
}

/**
 * specs/068 FR-005 — what a turn actually did. `Acted` carries no aggregate pass/fail on purpose:
 * a turn can succeed at one part and fail at another (FR-006), so read {@link TurnOutcome.attempts}.
 */
export type TurnVerdict = 'AnsweredInWords' | 'Acted' | 'FailedBeforeCompleting'

/** specs/068 contracts/turn-outcome.md §1 — one attempted action, as the client is allowed to see it. The server-resolved arguments are deliberately absent. */
export interface ActionAttempt {
  kind: string
  key: string | null
  targetLabel: string | null
  succeeded: boolean
  failureReason: string | null
}

/**
 * specs/068 FR-004a — the turn's recorded outcome, arriving as the trailing `__TURN_OUTCOME__`
 * event and again on the persisted message when the conversation is reopened. Undefined means
 * "outcome unknown" (a message written before this existed), which is never read as success.
 */
export interface TurnOutcome {
  verdict: TurnVerdict
  attempts: ActionAttempt[]
  failureReason: string | null
  recordedAtUtc: string
}

export interface ChatMessage {
  /** The persisted `Message.Id` (specs/002-chat-history-management) — undefined only for the brief window between a live send and the trailing `__MEMORY__`/history-refetch event resolving it. */
  id?: string
  role: 'system' | 'user' | 'assistant'
  content: string
  /**
   * A generated image stored as a platform document. When set, the bubble shows that image
   * (resolved to a fresh signed URL at render time) and `content` is its alt text. Older image
   * messages predate this and carry a markdown image in `content` instead.
   */
  imageDocumentId?: string
  /** Display-only metadata (specs/002-chat-history-management FR-016/FR-017) — never sent to the AI provider, only rendered. */
  provider?: string | null
  model?: string | null
  /** FR-030: a connection dropped mid-stream — the content shown is whatever arrived before that, not the full reply. */
  isIncomplete?: boolean
  attachments?: { id: string; fileName: string; accessLocation: string }[]
  citations?: Citation[]
  /** specs/016-rag-semantic-search US1 (research.md Decision 8) — undefined when no knowledge base was attached to the conversation ("not applicable"). */
  retrievalOutcome?: RagRetrievalOutcome
  /** Populated only when `retrievalOutcome === 'Unavailable'` (FR-037a) — a non-silent, visible warning; the message content itself is still complete. */
  retrievalError?: string | null
  /** specs/018-ai-memory-system US1 (FR-014) — undefined when memory is disabled/not yet evaluated for this turn ("not applicable"); `'Found'` means the "why does Lucy know this" trace has at least one entry. */
  memoryOutcome?: MemoryRetrievalOutcome
  /** specs/045-conversational-agent-runtime FR-021/FR-026 — the offer this assistant message made, or undefined when it offered nothing (the common case). */
  suggestedActions?: SuggestedAction[]
  /** What Lucy asked before the rows in {@link suggestedActions}. Undefined exactly when that is. */
  question?: string
  /**
   * The label of the row the user actually picked from {@link suggestedActions}, resolved from
   * the reply message that immediately follows this offer (its `selectedActionKind`/
   * `selectedActionKey`, matched back against this offer's own rows). Undefined when the offer
   * was never answered (still live, or the conversation moved on without a selection) — in that
   * case the retired card falls back to listing every option, same as before this existed.
   * `null` specifically for a decline, so the card can say so distinctly from "not yet answered."
   */
  selectedActionLabel?: string | null
  /** specs/068 FR-004a — what this assistant turn actually did. Undefined for user messages, and for assistant messages persisted before outcomes were recorded; never read as a success. */
  turnOutcome?: TurnOutcome
}

/** specs/005-multi-provider-ai-engine contracts/chat.md — mirrors `GenerationParametersDto`. Every field optional; an unset field falls back through the server-side inheritance chain. */
export interface GenerationParameters {
  temperature?: number
  topP?: number
  topK?: number
  presencePenalty?: number
  frequencyPenalty?: number
  maxTokens?: number
  stopSequences?: string[]
  seed?: number
  reasoningLevel?: string
  responseFormat?: string
  jsonMode?: boolean
  streaming?: boolean
  systemPrompt?: string
  developerPrompt?: string
}

/**
 * One event from {@link streamChat} — a plain content delta, (specs/016-rag-semantic-search US1)
 * the RAG retrieval outcome carried on the trailing `__RAG__` event,
 * (specs/018-ai-memory-system US1) the memory outcome + real persisted message id carried on the
 * trailing `__MEMORY__` event, (specs/036-startup-geolocation US3) the agent-confirmed location
 * carried on the trailing `__LOCATION__` event, (specs/038-viewer-poi-zoom US2) an explicit
 * zoom command carried on the trailing `__ZOOM__` event, or a `messageBreak` telling the caller
 * that everything after it belongs to a second assistant message.
 */
export type ChatStreamEvent =
  | { type: 'content'; delta: string }
  | { type: 'retrieval'; outcome: RagRetrievalOutcome; citations: Omit<Citation, 'id'>[]; error: string | null }
  /** `messageId` is null only when the turn persisted no assistant message at all. */
  | { type: 'memory'; messageId: string | null; outcome: MemoryRetrievalOutcome }
  | {
      type: 'location'
      latitude: number
      longitude: number
      locationName: string
      confidence: number
      /** Null only from a server older than the level (a stream in flight across a deploy). */
      confidenceLevel: 'low' | 'medium' | 'high' | null
      /** Why that level — the site card's shield tooltip. Null from an older server, as above. */
      confidenceReason: string | null
      source: string
      locationType: string | null
      viewport: { northeastLat: number; northeastLng: number; southwestLat: number; southwestLng: number } | null
    }
  | { type: 'zoom'; direction: 'in' | 'out' }
  /** specs/051-viewer-scene-content-api FR-004/research D8 — content Lucy asked the viewer to
   * load, carried on the trailing `__VIEWER_CONTENT__` event, mirroring `zoom`'s own shape. */
  | {
      type: 'viewerContent'
      fileId: string
      latitude: number
      longitude: number
      heightMetres: number
      orientationDegrees: number
      scale: number
    }
  /** specs/052-solar-analysis research D3 — Lucy opening solar analysis for the active site,
   * carried on the trailing `__SOLAR_ANALYSIS__` event, mirroring `viewerContent`'s own shape.
   * Carries no solar figures — the browser computes them once. */
  | {
      type: 'solarAnalysis'
      date: string
      timeOfDay: string
    }
  /**
   * The server finished one assistant message and started another. Deltas that follow belong to
   * the new one; the text so far is complete and already persisted server-side.
   *
   * `pendingLabel` is present when the break was announced *before* the work that fills the new
   * message — "Finding the site boundary" — so the caller can say what is happening instead of
   * leaving the reply looking finished and silent for tens of seconds.
   */
  | { type: 'messageBreak'; pendingLabel: string | null }
  | {
      type: 'siteBoundary'
      siteName: string
      centroid: { latitude: number; longitude: number }
      polygon: { latitude: number; longitude: number }[]
      /** specs/077 — the site's rings that do not touch `polygon`; empty for a one-outline site. */
      additionalPolygons: { latitude: number; longitude: number }[][]
      areaSquareMeters: number
      confidence: number
      confidenceLevel: 'low' | 'medium' | 'high'
      source: string
      sourceDetail: string
      alternativeCandidateNames: string[]
    }
  /** specs/045-conversational-agent-runtime FR-021 — the offer closing a turn; last before `[DONE]`, and often absent (FR-025). */
  | {
      type: 'actions'
      offeredByMessageId: string
      question: string
      actions: SuggestedAction[]
    }
  /** specs/068 FR-004a — the turn's own verdict, trailing the stream. Emitted on every turn, including one that only answered in words and one that failed partway. */
  | { type: 'turnOutcome'; outcome: TurnOutcome }

const RAG_EVENT_PREFIX = '__RAG__'
const MEMORY_EVENT_PREFIX = '__MEMORY__'
const LOCATION_EVENT_PREFIX = '__LOCATION__'
const ZOOM_EVENT_PREFIX = '__ZOOM__'
const VIEWER_CONTENT_EVENT_PREFIX = '__VIEWER_CONTENT__'
const SOLAR_ANALYSIS_EVENT_PREFIX = '__SOLAR_ANALYSIS__'
const SITE_BOUNDARY_EVENT_PREFIX = '__SITE_BOUNDARY__'
const ACTIONS_EVENT_PREFIX = '__ACTIONS__'
const TURN_OUTCOME_EVENT_PREFIX = '__TURN_OUTCOME__'
const MESSAGE_BREAK_EVENT = '__MESSAGE_BREAK__'

/** specs/045-conversational-agent-runtime US3, contracts/suggested-actions-api.md §1 — echoes back which offered row was chosen; the server resolves it against the offer it came from and dispatches the grounded row, never these `key`/`text`/`arguments` values directly. */
export interface SelectedActionRequest {
  offeredByMessageId: string
  kind: SuggestedAction['kind']
  key: string | null
  text: string | null
  arguments: unknown
}

/**
 * specs/068 US2, contracts/retry-api.md — asks for a previously failed action to be run again.
 *
 * A message id and nothing else, deliberately: the capability, its arguments and its target all
 * come back from what the server recorded on that turn (FR-010). Sending them from here would make
 * `/ai/chat` a general capability-invocation route that merely looks like a retry. Mutually
 * exclusive with {@link SelectedActionRequest} — sending both is a 400.
 */
export interface RetryRequest {
  failedMessageId: string
}

/**
 * Streams a chat completion via SSE (research.md Topic 2). Uses `fetch` + a
 * `ReadableStream` reader rather than the browser's native `EventSource`, since
 * `EventSource` cannot send a custom `Authorization` header.
 */
export async function* streamChat(
  chatId: string,
  messages: ChatMessage[],
  providerId: string,
  modelId: string,
  generationParameters: GenerationParameters | undefined,
  signal?: AbortSignal,
  selectedAction?: SelectedActionRequest,
  retry?: RetryRequest,
): AsyncGenerator<ChatStreamEvent> {
  const sendRequest = () => {
    const accessToken = useAuthStore.getState().accessToken
    return fetch(`${API_BASE_URL}/ai/chat`, {
      method: 'POST',
      signal,
      headers: {
        'Content-Type': 'application/json',
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
      },
      body: JSON.stringify({ chatId, messages, providerId, modelId, generationParameters, selectedAction, retry }),
    })
  }

  let response = await sendRequest()

  // Bypasses `apiFetch`, so a revoked session (specs/060) needs its own silent-refresh-then-
  // redirect handling — otherwise it falls through to the generic "chat request failed" error
  // below instead of the sign-out/login flow every other authenticated call gets.
  if (response.status === 401) {
    if (await attemptSilentRefresh()) {
      response = await sendRequest()
    } else {
      // Never settles — matches apiFetch's redirect-then-hang behavior so nothing downstream
      // (useChatStream's catch) can render a "failed to send" error that outlives the navigation.
      await redirectToLogin<never>()
      return
    }
  }

  if (!response.ok || !response.body) {
    // RFC 7807 Problem Details (constitution §6) — surface the vendor-agnostic translated
    // message (e.g. "AI provider rate limited") rather than a generic status-code string,
    // so the user sees why the send actually failed (FR-028).
    const problem = await response.json().catch(() => undefined)
    throw new Error(problem?.detail ?? problem?.title ?? `Chat request failed with ${response.status}`)
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) return

    buffer += decoder.decode(value, { stream: true })
    const lines = buffer.split('\n\n')
    buffer = lines.pop() ?? ''

    for (const line of lines) {
      if (!line.startsWith('data:')) continue
      // Per the SSE spec, strip at most the single protocol-mandated leading space after
      // "data:" — NOT a full .trim(). The backend writes `data: {chunk}` (AiController.cs),
      // and `chunk` itself frequently starts with its own meaningful space (OpenAI streams
      // most word tokens with a leading space, e.g. " I", " can", " hear" — that space IS the
      // word boundary). A full .trim() here silently ate every one of those, running every
      // streamed word together with no spaces.
      const data = line.slice('data:'.length).replace(/^ /, '')
      if (data === '[DONE]') return

      if (data.startsWith(RAG_EVENT_PREFIX)) {
        const payload = JSON.parse(data.slice(RAG_EVENT_PREFIX.length)) as {
          retrievalOutcome: RagRetrievalOutcome
          citations: {
            documentChunkId: string
            knowledgeBaseId: string
            documentId: string
            documentVersionId: string
            documentTitle: string
            knowledgeBaseName: string
            pageNumber: number | null
            section: string | null
            excerpt: string
          }[]
          retrievalError: string | null
        }
        yield {
          type: 'retrieval',
          outcome: payload.retrievalOutcome,
          error: payload.retrievalError,
          citations: payload.citations.map((c) => ({
            sourceLabel: c.documentTitle,
            sourceReference: null,
            documentChunkId: c.documentChunkId,
            knowledgeBaseId: c.knowledgeBaseId,
            documentId: c.documentId,
            documentVersionId: c.documentVersionId,
            pageNumber: c.pageNumber,
            section: c.section,
            excerpt: c.excerpt,
          })),
        }
        continue
      }

      if (data.startsWith(MEMORY_EVENT_PREFIX)) {
        const payload = JSON.parse(data.slice(MEMORY_EVENT_PREFIX.length)) as {
          messageId: string | null
          memoryOutcome: MemoryRetrievalOutcome
        }
        yield { type: 'memory', messageId: payload.messageId, outcome: payload.memoryOutcome }
        continue
      }

      // specs/036-startup-geolocation US3 / specs/038-viewer-poi-zoom: agent-confirmed location —
      // extended with locationType and viewport fields for altitude-accurate zoom.
      if (data.startsWith(LOCATION_EVENT_PREFIX)) {
        const payload = JSON.parse(data.slice(LOCATION_EVENT_PREFIX.length)) as {
          latitude: number
          longitude: number
          locationName: string
          confidence: number
          confidenceLevel?: 'low' | 'medium' | 'high'
          confidenceReason?: string
          source: string
          locationType: string | null
          viewport: { northeastLat: number; northeastLng: number; southwestLat: number; southwestLng: number } | null
        }
        yield {
          type: 'location',
          latitude: payload.latitude,
          longitude: payload.longitude,
          locationName: payload.locationName,
          confidence: payload.confidence,
          confidenceLevel: payload.confidenceLevel ?? null,
          confidenceReason: payload.confidenceReason ?? null,
          source: payload.source,
          locationType: payload.locationType ?? null,
          viewport: payload.viewport ?? null,
        }
        continue
      }

      // specs/042-site-boundary-resolution: resolved site boundary — same distinguishable-prefix
      // pattern as __LOCATION__.
      if (data.startsWith(SITE_BOUNDARY_EVENT_PREFIX)) {
        const payload = JSON.parse(data.slice(SITE_BOUNDARY_EVENT_PREFIX.length)) as {
          siteName: string
          centroid: { latitude: number; longitude: number }
          polygon: { latitude: number; longitude: number }[]
          additionalPolygons?: { latitude: number; longitude: number }[][]
          areaSquareMeters: number
          confidence: number
          confidenceLevel: 'low' | 'medium' | 'high'
          source: string
          sourceDetail: string
          alternativeCandidateNames: string[]
        }
        yield {
          type: 'siteBoundary',
          siteName: payload.siteName,
          centroid: payload.centroid,
          polygon: payload.polygon,
          // Absent from a server that predates specs/077.
          additionalPolygons: payload.additionalPolygons ?? [],
          areaSquareMeters: payload.areaSquareMeters,
          confidence: payload.confidence,
          confidenceLevel: payload.confidenceLevel,
          source: payload.source,
          sourceDetail: payload.sourceDetail,
          alternativeCandidateNames: payload.alternativeCandidateNames,
        }
        continue
      }

      // specs/068 FR-004a — the turn's recorded verdict. Parsed before any content handling so
      // it is never rendered as prose: the whole point of this event is that the reply's words are
      // not evidence of what happened.
      if (data.startsWith(TURN_OUTCOME_EVENT_PREFIX)) {
        const outcome = JSON.parse(data.slice(TURN_OUTCOME_EVENT_PREFIX.length)) as TurnOutcome
        yield { type: 'turnOutcome', outcome }
        continue
      }

      // specs/045-conversational-agent-runtime FR-021 — the offer closing a turn.
      if (data.startsWith(ACTIONS_EVENT_PREFIX)) {
        const payload = JSON.parse(data.slice(ACTIONS_EVENT_PREFIX.length)) as {
          offeredByMessageId: string
          question: string | null
          actions: {
            kind: string
            capabilityKey: string | null
            text: string | null
            label: string
            description: string
            arguments: unknown
            isDecline: boolean
          }[]
        }
        yield {
          type: 'actions',
          offeredByMessageId: payload.offeredByMessageId,
          question: payload.question ?? '',
          actions: payload.actions.map((a) => ({
            kind: a.kind as SuggestedAction['kind'],
            capabilityKey: a.capabilityKey,
            text: a.text,
            label: a.label,
            description: a.description,
            arguments: a.arguments,
            isDecline: a.isDecline,
          })),
        }
        continue
      }

      // specs/038-viewer-poi-zoom US2: explicit zoom command — `data: __ZOOM__in` / `__ZOOM__out`
      if (data.startsWith(ZOOM_EVENT_PREFIX)) {
        const direction = data.slice(ZOOM_EVENT_PREFIX.length)
        if (direction === 'in' || direction === 'out') {
          yield { type: 'zoom', direction }
        }
        continue
      }

      // specs/051-viewer-scene-content-api FR-004: content Lucy asked the viewer to load —
      // `data: __VIEWER_CONTENT__{...}`.
      if (data.startsWith(VIEWER_CONTENT_EVENT_PREFIX)) {
        const payload = JSON.parse(data.slice(VIEWER_CONTENT_EVENT_PREFIX.length)) as {
          fileId: string
          latitude: number
          longitude: number
          heightMetres: number
          orientationDegrees: number
          scale: number
        }
        yield { type: 'viewerContent', ...payload }
        continue
      }

      // specs/052-solar-analysis research D3: Lucy opening solar analysis for the active site —
      // `data: __SOLAR_ANALYSIS__{...}`.
      if (data.startsWith(SOLAR_ANALYSIS_EVENT_PREFIX)) {
        const payload = JSON.parse(data.slice(SOLAR_ANALYSIS_EVENT_PREFIX.length)) as { date: string; timeOfDay: string }
        yield { type: 'solarAnalysis', ...payload }
        continue
      }

      // Checked before the content fallback. The marker is either bare or followed
      // immediately by its JSON payload, so a line that starts with it and continues with
      // anything else is ordinary text, not a malformed event.
      if (data === MESSAGE_BREAK_EVENT) {
        yield { type: 'messageBreak', pendingLabel: null }
        continue
      }
      if (data.startsWith(`${MESSAGE_BREAK_EVENT}{`)) {
        const payload = JSON.parse(data.slice(MESSAGE_BREAK_EVENT.length)) as { pendingLabel?: string }
        yield { type: 'messageBreak', pendingLabel: payload.pendingLabel ?? null }
        continue
      }

      yield { type: 'content', delta: data }
    }
  }
}

export const translate = (chatId: string, text: string, targetLanguage: string) =>
  apiFetch<string>('/ai/translate', { method: 'POST', body: JSON.stringify({ chatId, text, targetLanguage }) })

/**
 * Generates an image with the administrator's ImageGeneration model and stores it as the user's
 * own document. Returns that document's id — never a provider URL, which would expire and is not
 * the platform's to hand out.
 */
export async function generateImage(chatId: string, prompt: string): Promise<string> {
  const result = await apiFetch<{ documentId: string }>('/ai/images', {
    method: 'POST',
    body: JSON.stringify({ chatId, prompt }),
  })
  return result.documentId
}

export async function transcribeAudio(file: File): Promise<string> {
  const form = new FormData()
  form.append('file', file)

  const sendRequest = () => {
    const accessToken = useAuthStore.getState().accessToken
    return fetch(`${API_BASE_URL}/ai/transcriptions`, {
      method: 'POST',
      headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
      body: form,
    })
  }

  let response = await sendRequest()

  // Bypasses `apiFetch`, so a revoked session needs its own silent-refresh-then-redirect handling
  // (see the matching comment in streamChat above).
  if (response.status === 401) {
    if (await attemptSilentRefresh()) {
      response = await sendRequest()
    } else {
      return redirectToLogin<string>()
    }
  }

  if (!response.ok) {
    const problem = await response.json().catch(() => undefined)
    // Prefer `detail` over `title` for the message — useVoiceRecorder surfaces `err.message`
    // directly to the user, and `detail` (e.g. "The AI provider could not process this
    // request. Please try again.") is more actionable than the generic `title`.
    throw new ApiError(
      response.status,
      problem?.detail ?? problem?.title ?? 'Transcription failed',
      problem?.detail,
    )
  }

  const result = (await response.json()) as { text: string }
  return result.text
}

