import { create } from 'zustand'

/** spec.md Key Entity "Voice State" — the centralized state every voice-related UI control
 * (the mic button, and formerly `VoiceControlBar`, retired by specs/029-fix-chat-widget-bugs
 * research.md Decision 5) derives from, rather than each component tracking its own local
 * notion of "what's happening" (FR-020). Not persisted — this is transient, per-tab runtime
 * state, unlike `voicePreferencesStore.ts`. */
export type VoiceStateName =
  | 'Idle'
  | 'Listening'
  | 'UserSpeaking'
  | 'Processing'
  | 'AiThinking'
  | 'AiSpeaking'
  | 'Interrupted'
  | 'Muted'
  | 'Error'

interface VoiceStateStore {
  state: VoiceStateName
  /** Set only when `state === 'Error'` (FR-032/FR-036) — a visible, actionable message, never
   * a silent failure. */
  errorMessage: string | null
  setState: (state: VoiceStateName) => void
  setError: (message: string) => void
  reset: () => void
}

export const useVoiceState = create<VoiceStateStore>((set) => ({
  state: 'Idle',
  errorMessage: null,
  setState: (state) => set({ state, errorMessage: null }),
  setError: (message) => set({ state: 'Error', errorMessage: message }),
  reset: () => set({ state: 'Idle', errorMessage: null }),
}))
