import { apiFetch } from '../../../api/httpClient'

/** specs/070 — mirrors `AdminVoiceProviderDto`. Never includes the credential value itself. */
export interface AdminVoiceProvider {
  id: string
  providerKey: string
  displayName: string
  /** 0 is Lucy's voice; the rest are failovers, tried in ascending order. */
  priority: number
  isPrimary: boolean
  defaultVoiceId: string | null
  requiresCredential: boolean
  hasCredential: boolean
  /** Vendor-style fingerprint of the configured key, or `null`. Never the full key. */
  credentialHint: string | null
}

/** specs/070 — one engine the platform can speak through; `isAdded` once an administrator has added it. */
export interface VoiceEngine {
  providerKey: string
  displayName: string
  requiresCredential: boolean
  isAdded: boolean
}

/** specs/070 — mirrors `VoiceOptionDto`. */
export interface VoiceOption {
  id: string
  name: string
  gender: string | null
  description: string | null
}

/** specs/070 — a synthesized sample, base64 so it rides the ordinary JSON error handling. */
export interface VoicePreview {
  audioBase64: string
  contentType: string
}

export const VOICE_QUERY_KEYS = {
  providers: ['admin', 'voice-providers'],
  engines: ['admin', 'voice-engines'],
  voices: (providerId: string) => ['admin', 'voice-provider-voices', providerId],
}

export const getVoiceEngines = () => apiFetch<VoiceEngine[]>('/admin/voice/engines')

export const getVoiceProviders = () => apiFetch<AdminVoiceProvider[]>('/admin/voice/providers')

export const addVoiceProvider = (providerKey: string, apiKey: string | null) =>
  apiFetch<AdminVoiceProvider>('/admin/voice/providers', {
    method: 'POST',
    body: JSON.stringify({ providerKey, apiKey }),
  })

export const setVoiceProviderCredential = (id: string, apiKey: string) =>
  apiFetch<AdminVoiceProvider>(`/admin/voice/providers/${id}/credential`, {
    method: 'PUT',
    body: JSON.stringify({ apiKey }),
  })

/** Asked of the provider live — ElevenLabs lists the account's voices, Supertonic its installed styles. */
export const getVoiceProviderVoices = (id: string) => apiFetch<VoiceOption[]>(`/admin/voice/providers/${id}/voices`)

export const setPrimaryVoiceProvider = (providerId: string, voiceId: string) =>
  apiFetch<AdminVoiceProvider[]>('/admin/voice/primary', {
    method: 'PUT',
    body: JSON.stringify({ providerId, voiceId }),
  })

export const previewVoice = (providerId: string, voiceId: string, text: string, language: string) =>
  apiFetch<VoicePreview>(`/admin/voice/providers/${providerId}/preview`, {
    method: 'POST',
    body: JSON.stringify({ voiceId, text, language }),
  })
