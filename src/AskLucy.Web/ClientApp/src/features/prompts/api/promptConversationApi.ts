import { API_BASE_URL } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'

/**
 * Streams a prompt insertion into a live conversation (spec.md FR-080, User Story 5,
 * contracts/prompt-conversation-integration-api.md). Content-delta-only wire format — no
 * RAG/memory trailing events (those ride the existing chat send path's own SSE stream, not this
 * one) — mirrors `aiApi.ts`'s `streamChat` shape but simpler, matching the backend's plainer
 * `data: {delta}` / `data: [DONE]` format.
 */
export async function* insertPromptIntoConversation(
  chatId: string,
  promptId: string,
  variableValues: Record<string, string | null>,
  signal?: AbortSignal,
): AsyncGenerator<string> {
  const accessToken = useAuthStore.getState().accessToken

  const response = await fetch(`${API_BASE_URL}/chats/${chatId}/prompt-messages`, {
    method: 'POST',
    signal,
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    },
    body: JSON.stringify({ promptId, variableValues }),
  })

  if (!response.ok || !response.body) {
    const problem = await response.json().catch(() => undefined)
    throw new Error(problem?.detail ?? problem?.title ?? `Insert prompt request failed with ${response.status}`)
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
      const data = line.slice('data:'.length).replace(/^ /, '')
      if (data === '[DONE]') return
      yield data
    }
  }
}
