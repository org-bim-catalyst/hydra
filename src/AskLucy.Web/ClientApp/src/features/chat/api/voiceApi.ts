import { apiFetch, API_BASE_URL } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'
import type { ChatMessage, GenerationParameters } from './aiApi'

/** contracts/voice-stt-session.md. */
export interface SpeechToTextSession {
  token: string
  expiresAtUtc: string
}

export const createSttSession = (language: string) =>
  apiFetch<SpeechToTextSession>('/ai/voice/stt-session', {
    method: 'POST',
    body: JSON.stringify({ language }),
  })

/** contracts/voice-preferences.md. */
export type VoiceConversationMode = 'PushToTalk' | 'Continuous'

export interface UserVoicePreference {
  conversationMode: VoiceConversationMode
  isMuted: boolean
  selectedVoiceId: string | null
  voiceSpeed: number | null
  voiceStyle: number | null
  preferredMicrophoneDeviceId: string | null
  preferredSpeakerDeviceId: string | null
}

export const getVoicePreferences = () => apiFetch<UserVoicePreference>('/ai/voice/preferences')

export const saveVoicePreferences = (preference: UserVoicePreference) =>
  apiFetch<UserVoicePreference>('/ai/voice/preferences', {
    method: 'PUT',
    body: JSON.stringify(preference),
  })

/** contracts/voice-provider-health.md — admin-only aggregate failover/recovery view. */
export interface VoiceProviderFailoverEvent {
  occurredAtUtc: string
  direction: 'FailedOverToFallback' | 'RecoveredToPrimary'
  reason: string | null
}

export interface VoiceProviderHealth {
  currentStatus: 'healthy' | 'degraded'
  failoverCount: number
  recoveryCount: number
  events: VoiceProviderFailoverEvent[]
}

export const getVoiceProviderHealth = (fromUtc?: string, toUtc?: string) => {
  const params = new URLSearchParams()
  if (fromUtc) params.set('from', fromUtc)
  if (toUtc) params.set('to', toUtc)
  const query = params.toString()
  return apiFetch<VoiceProviderHealth>(`/ai/voice/health${query ? `?${query}` : ''}`)
}

/** contracts/voice-reply-stream.md — one event from the multiplexed `/ai/voice/reply` stream. */
export type VoiceReplyEvent =
  | { type: 'transcript-delta'; content: string }
  | { type: 'audio-chunk'; sequence: number; audio: string }
  | { type: 'provider-status'; voiceProvider: 'primary' | 'fallback' }
  | { type: 'audio-failed' }
  | { type: 'usage'; inputTokens?: number; outputTokens?: number; latencyMs?: number }
  | { type: 'done' }
  | { type: 'error'; errorType: string; title: string; detail: string }

/**
 * Streams a voice-mode reply (text + synthesized speech) via the same SSE-over-fetch pattern
 * as {@link streamChat} in `aiApi.ts`, except each `data:` line is a JSON-enveloped event
 * (research.md Decision 3) rather than a raw text delta.
 */
export async function* streamVoiceReply(
  chatId: string,
  messages: ChatMessage[],
  providerId: string,
  modelId: string,
  generationParameters: GenerationParameters | undefined,
  language: string,
  signal?: AbortSignal,
): AsyncGenerator<VoiceReplyEvent> {
  const accessToken = useAuthStore.getState().accessToken

  const response = await fetch(`${API_BASE_URL}/ai/voice/reply`, {
    method: 'POST',
    signal,
    headers: {
      'Content-Type': 'application/json',
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    },
    body: JSON.stringify({ chatId, messages, providerId, modelId, generationParameters, language }),
  })

  if (!response.ok || !response.body) {
    const problem = await response.json().catch(() => undefined)
    throw new Error(problem?.detail ?? problem?.title ?? `Voice reply request failed with ${response.status}`)
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
      if (!data) continue
      yield JSON.parse(data) as VoiceReplyEvent
    }
  }
}
