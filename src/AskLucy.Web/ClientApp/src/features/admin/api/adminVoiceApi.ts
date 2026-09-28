import { apiFetch } from '../../../api/httpClient'

/** specs/072 FR-037 — whether an on-server engine can load its model; API-key engines are always `Ready`. */
export type VoiceProviderModelStatus = 'Ready' | 'ModelUnavailable'

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
  modelStatus: VoiceProviderModelStatus
  /** Why the model can't load, e.g. "The Supertonic model is marked unavailable in Custom Models."; `null` when `Ready`. */
  modelStatusReason: string | null
  /**
   * For an engine keyed under Admin → AI providers (ElevenLabs): whether that provider is switched
   * on there. `null` for an engine keyed here or needing no key. The key itself is set there too.
   */
  vendorEnabled: boolean | null
}

/** specs/070 — one engine the platform can speak through; `isAdded` once an administrator has added it. */
export interface VoiceEngine {
  providerKey: string
  displayName: string
  requiresCredential: boolean
  isAdded: boolean
  /** Its API key is set under Admin → AI providers, so adding it here asks for no key. */
  isKeyedAsAiProvider: boolean
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

/** specs/078 — the dictation engines an administrator can choose. */
export type DictationPrimaryEngine = 'LocalWhisper' | 'OpenAiWhisper' | 'ElevenLabsRealtime'
export type PushToTalkEngine = 'LocalWhisper' | 'OpenAiWhisper' | 'Browser'

export interface DictationEngineChoice<TEngine extends string = string> {
  engine: TEngine
  selectable: boolean
  /** e.g. "OpenAI is switched off under AI providers."; `null` when selectable. */
  unavailableReason: string | null
}

/** specs/078 — one Completed Custom Models deployment as a Local Whisper model choice. */
export interface LocalWhisperModelOption {
  id: string
  label: string
  selectable: boolean
  reason: string | null
}

/** specs/078 contracts/admin-dictation.md — mirrors `DictationSettingsDto`. */
export interface DictationSettings {
  primaryEngine: DictationPrimaryEngine
  pushToTalkEngine: PushToTalkEngine
  state: 'Active' | 'Suspended'
  suspension: { engine: string; atUtc: string; reason: string; browserInUse: boolean } | null
  lastRevert: { atUtc: string; reason: string; from: string } | null
  engines: DictationEngineChoice<DictationPrimaryEngine>[]
  pushToTalkEngines: DictationEngineChoice<PushToTalkEngine>[]
  localWhisper: {
    selectedModelId: string | null
    /** `problem` says why Local Whisper isn't serving, and that the browser built-in serves instead. */
    effectiveModel: { label: string | null; ready: boolean; problem: string | null }
    models: LocalWhisperModelOption[]
  }
  /** Sent back on every change; a stale one is refused with 409. */
  rowVersion: string
}

export interface LocalWhisperTryResult {
  text: string
  elapsedMs: number
  modelLabel: string
}

export const VOICE_QUERY_KEYS = {
  providers: ['admin', 'voice-providers'],
  engines: ['admin', 'voice-engines'],
  voices: (providerId: string) => ['admin', 'voice-provider-voices', providerId],
  dictation: ['admin', 'voice-dictation'],
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

export const getDictationSettings = () => apiFetch<DictationSettings>('/admin/voice/dictation')

/** `null` selects no model, so Local Whisper's paths use the browser built-in. Doesn't change the primary engine. */
export const selectLocalWhisperModel = (customModelId: string | null, rowVersion: string) =>
  apiFetch<void>('/admin/voice/dictation/local-whisper-model', {
    method: 'PUT',
    body: JSON.stringify({ customModelId, rowVersion }),
  })

/** Transcribes a 16 kHz mono WAV sample on a deployment without selecting it (FR-009c). */
export const tryLocalWhisperModel = (customModelId: string, wav: Blob, language?: string) => {
  const form = new FormData()
  form.append('file', new File([wav], 'sample.wav', { type: 'audio/wav' }))
  form.append('customModelId', customModelId)
  if (language) form.append('language', language)
  return apiFetch<LocalWhisperTryResult>('/admin/voice/dictation/try', { method: 'POST', body: form })
}

export const previewVoice = (providerId: string, voiceId: string, text: string, language: string) =>
  apiFetch<VoicePreview>(`/admin/voice/providers/${providerId}/preview`, {
    method: 'POST',
    body: JSON.stringify({ voiceId, text, language }),
  })
