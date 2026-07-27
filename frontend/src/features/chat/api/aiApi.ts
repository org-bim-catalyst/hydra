import { apiFetch } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

export interface ChatMessage {
  role: 'system' | 'user' | 'assistant'
  content: string
}

/**
 * Streams a chat completion via SSE (research.md Topic 2). Uses `fetch` + a
 * `ReadableStream` reader rather than the browser's native `EventSource`, since
 * `EventSource` cannot send a custom `Authorization` header.
 */
export async function* streamChat(messages: ChatMessage[], signal?: AbortSignal): AsyncGenerator<string> {
  const accessToken = useAuthStore.getState().accessToken

  const response = await fetch(`${API_BASE_URL}/ai/chat`, {
    method: 'POST',
    signal,
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    },
    body: JSON.stringify({ messages }),
  })

  if (!response.ok || !response.body) {
    throw new Error(`Chat request failed with ${response.status}`)
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
      const data = line.slice('data:'.length).trim()
      if (data === '[DONE]') return
      yield data
    }
  }
}

export const translate = (text: string, targetLanguage: string) =>
  apiFetch<string>('/ai/translate', { method: 'POST', body: JSON.stringify({ text, targetLanguage }) })

export async function generateImage(prompt: string): Promise<string> {
  const result = await apiFetch<{ url: string }>('/ai/images', { method: 'POST', body: JSON.stringify({ prompt }) })
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
