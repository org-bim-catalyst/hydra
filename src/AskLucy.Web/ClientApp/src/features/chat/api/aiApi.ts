import { apiFetch } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

export interface ChatMessage {
  role: 'system' | 'user' | 'assistant'
  content: string
  /** Display-only metadata (specs/002-chat-history-management FR-016/FR-017) — never sent to the AI provider, only rendered. */
  provider?: string | null
  model?: string | null
  /** FR-030: a connection dropped mid-stream — the content shown is whatever arrived before that, not the full reply. */
  isIncomplete?: boolean
  attachments?: { id: string; fileName: string; accessLocation: string }[]
  citations?: { id: string; sourceLabel: string; sourceReference: string | null }[]
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
): AsyncGenerator<string> {
  const accessToken = useAuthStore.getState().accessToken

  const response = await fetch(`${API_BASE_URL}/ai/chat`, {
    method: 'POST',
    signal,
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    },
    body: JSON.stringify({ chatId, messages, providerId, modelId, generationParameters }),
  })

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
      yield data
    }
  }
}

export const translate = (chatId: string, text: string, targetLanguage: string) =>
  apiFetch<string>('/ai/translate', { method: 'POST', body: JSON.stringify({ chatId, text, targetLanguage }) })

export async function generateImage(chatId: string, prompt: string): Promise<string> {
  const result = await apiFetch<{ url: string }>('/ai/images', {
    method: 'POST',
    body: JSON.stringify({ chatId, prompt }),
  })
  return result.url
}

export async function transcribeAudio(file: File): Promise<string> {
  const accessToken = useAuthStore.getState().accessToken
  const form = new FormData()
  form.append('file', file)

  const response = await fetch(`${API_BASE_URL}/ai/transcriptions`, {
    method: 'POST',
    headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
    body: form,
  })

  if (!response.ok) {
    throw new Error(`Transcription failed with ${response.status}`)
  }

  const result = (await response.json()) as { text: string }
  return result.text
}

/** Mic-dictation counterpart to {@link transcribeAudio} — see the backend endpoint's comment. */
export async function transcribeMicrophoneAudio(wavBlob: Blob): Promise<string> {
  const accessToken = useAuthStore.getState().accessToken
  const form = new FormData()
  form.append('file', wavBlob, 'recording.wav')

  const response = await fetch(`${API_BASE_URL}/ai/transcriptions/microphone`, {
    method: 'POST',
    headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
    body: form,
  })

  if (!response.ok) {
    throw new Error(`Transcription failed with ${response.status}`)
  }

  const result = (await response.json()) as { text: string }
  return result.text
}
