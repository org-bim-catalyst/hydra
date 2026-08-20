import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import { saveVoicePreferences, type UserVoicePreference } from '../api/voiceApi'

interface VoicePreferencesState extends UserVoicePreference {
  /** Surfaced by the chat page as a Snackbar (same pattern as `useTextToSpeech`'s `error`
   * field, ChatPage.tsx) — a failed preference *save* is never silent (constitution §2.VIII).
   * specs/029-fix-chat-widget-bugs research.md Decision 3/4: this field is scoped to `update`
   * failures only now — the initial-fetch failure this store previously also surfaced here
   * (via the now-removed `hydrateFromServer`) moved to `useVoicePreferencesQuery`'s own
   * TanStack Query error state, which drives a smaller, non-blocking indicator instead of
   * this store-wide Snackbar firing on every chat load. */
  error: string | null
  update: (patch: Partial<UserVoicePreference>) => Promise<void>
  clearError: () => void
}

const DEFAULTS: UserVoicePreference = {
  conversationMode: 'PushToTalk',
  isMuted: false,
  selectedVoiceId: null,
  voiceSpeed: null,
  voiceStyle: null,
  preferredMicrophoneDeviceId: null,
  preferredSpeakerDeviceId: null,
  defaultLanguage: null,
}

/**
 * Zustand `persist`/localStorage cache of the last-synced voice preferences (mirrors
 * `themeStore.ts`), kept in sync with the backend via `voiceApi` (FR-029/FR-030). The
 * localStorage copy lets voice mode/mute/etc. restore instantly on load without waiting for
 * the network round trip; `hydrateFromServer` then reconciles with the authoritative value.
 */
export const useVoicePreferencesStore = create<VoicePreferencesState>()(
  persist(
    (set, get) => ({
      ...DEFAULTS,
      error: null,

      update: async (patch) => {
        const previous = get()
        set(patch)

        try {
          const current = get()
          const saved = await saveVoicePreferences({
            conversationMode: current.conversationMode,
            isMuted: current.isMuted,
            selectedVoiceId: current.selectedVoiceId,
            voiceSpeed: current.voiceSpeed,
            voiceStyle: current.voiceStyle,
            preferredMicrophoneDeviceId: current.preferredMicrophoneDeviceId,
            preferredSpeakerDeviceId: current.preferredSpeakerDeviceId,
            defaultLanguage: current.defaultLanguage,
            ...patch,
          })
          set(saved)
        } catch (error) {
          // Roll back to the last-known-good state so the UI doesn't claim a preference took
          // effect when the server never actually saved it.
          set({
            ...previous,
            error: error instanceof Error ? error.message : 'Failed to save voice preferences.',
          })
        }
      },

      clearError: () => set({ error: null }),
    }),
    {
      name: 'ask-lucy-voice-preferences',
      partialize: (state) => ({
        conversationMode: state.conversationMode,
        isMuted: state.isMuted,
        selectedVoiceId: state.selectedVoiceId,
        voiceSpeed: state.voiceSpeed,
        voiceStyle: state.voiceStyle,
        preferredMicrophoneDeviceId: state.preferredMicrophoneDeviceId,
        preferredSpeakerDeviceId: state.preferredSpeakerDeviceId,
        defaultLanguage: state.defaultLanguage,
      }),
    },
  ),
)
