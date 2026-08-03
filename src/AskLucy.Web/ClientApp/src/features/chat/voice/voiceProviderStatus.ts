import { create } from 'zustand'
import { createSttSession } from '../api/voiceApi'

export type VoiceProvider = 'primary' | 'fallback'

interface VoiceProviderStatusStore {
  provider: VoiceProvider
  /** FR-033: shown alongside the fallback engine so the user knows quality is temporarily
   * reduced — never a silent, unexplained switch. */
  degradedNoticeVisible: boolean
  failOver: () => void
  recover: () => void
}

/** spec.md Key Entity "Voice Provider Status" (research.md Decision 5) — a Zustand store
 * (not a plain module-level variable) so every hook that needs to read or react to the
 * current engine (`useSpeechRecognition`, `useSpeechSynthesis`, `useConversationAudio`) stays
 * in sync without prop-drilling. Not persisted — this is per-session state, reset on reload. */
export const useVoiceProviderStatus = create<VoiceProviderStatusStore>((set) => ({
  provider: 'primary',
  degradedNoticeVisible: false,
  failOver: () => set({ provider: 'fallback', degradedNoticeVisible: true }),
  recover: () => set({ provider: 'primary', degradedNoticeVisible: false }),
}))

/**
 * Before each voice turn while on the fallback engine, retries `createSttSession` as a cheap
 * health probe (research.md Decision 5); a success flips the session back to primary
 * (FR-034/SC-010) before that turn begins. Call this at the start of every turn — it's a
 * no-op (resolves immediately) when already on the primary engine.
 */
export async function probeRecoveryIfDegraded(language: string): Promise<void> {
  const { provider, recover } = useVoiceProviderStatus.getState()
  if (provider !== 'fallback') return

  try {
    await createSttSession(language)
    recover()
  } catch {
    // Still down — stay on fallback; the next turn will probe again.
  }
}
