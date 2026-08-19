import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import {
  getPanelPreferences,
  savePanelPreferences,
  type UserPanelPreference,
} from '../../../features/settings/api/panelPreferencesApi'

interface PanelPreferencesState extends UserPanelPreference {
  /** Surfaced by the Settings "Viewer" tab as a Snackbar (same pattern as
   * `voicePreferencesStore`'s `error` field) — a failed preference save is never silent
   * (constitution §2.VIII). */
  error: string | null
  hydrateFromServer: () => Promise<void>
  update: (patch: Partial<UserPanelPreference>) => Promise<void>
  clearError: () => void
}

const DEFAULTS: UserPanelPreference = {
  opacityPercent: 85,
}

/**
 * Zustand `persist`/localStorage cache of the last-synced panel opacity preference (mirrors
 * `voicePreferencesStore.ts`), kept in sync with the backend via `panelPreferencesApi` (FR-011/
 * FR-012). The localStorage copy lets every open `FloatingPanel` restore its opacity instantly on
 * load without waiting for the network round trip; `hydrateFromServer` then reconciles with the
 * authoritative value.
 */
export const usePanelPreferencesStore = create<PanelPreferencesState>()(
  persist(
    (set, get) => ({
      ...DEFAULTS,
      error: null,

      hydrateFromServer: async () => {
        try {
          const preference = await getPanelPreferences()
          set(preference)
        } catch (error) {
          set({
            error: error instanceof Error ? error.message : 'Failed to load panel preferences.',
          })
        }
      },

      update: async (patch) => {
        const previous = get()
        set(patch)

        try {
          const current = get()
          const saved = await savePanelPreferences({
            opacityPercent: current.opacityPercent,
            ...patch,
          })
          set(saved)
        } catch (error) {
          // Roll back to the last-known-good state so the UI doesn't claim a preference took
          // effect when the server never actually saved it.
          set({
            ...previous,
            error: error instanceof Error ? error.message : 'Failed to save panel preferences.',
          })
        }
      },

      clearError: () => set({ error: null }),
    }),
    {
      name: 'ask-lucy-panel-preferences',
      partialize: (state) => ({ opacityPercent: state.opacityPercent }),
    },
  ),
)
