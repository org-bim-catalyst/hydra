import { useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { getVoicePreferences } from '../api/voiceApi'
import { useVoicePreferencesStore } from './voicePreferencesStore'

export const VOICE_PREFERENCES_QUERY_KEY = ['ai', 'voice', 'preferences']

/**
 * specs/029-fix-chat-widget-bugs research.md Decision 4 — replaces
 * `voicePreferencesStore.hydrateFromServer`'s hand-rolled `fetch`+`try/catch` (Bug 1's direct
 * enabler: no retry/staleness/error-state separation, wired straight into a blocking,
 * always-on Snackbar). Matches the sibling `useAiPreferences` pattern (constitution §7 —
 * server state belongs in TanStack Query, not a hand-rolled fetch inside Zustand).
 *
 * On success, syncs the fetched preference into `voicePreferencesStore` so
 * `isMutedPreference`/`conversationMode`/etc. remain synchronously readable elsewhere exactly
 * as before (unchanged consumers). On failure, the store's already-cached/default values are
 * left untouched (FR-002 — chat and voice stay usable on defaults) and this hook's own
 * `isError`/`error` drive a small, non-blocking indicator (research.md Decision 3) instead of
 * the previous blocking, always-on Snackbar.
 */
export function useVoicePreferencesQuery() {
  const query = useQuery({ queryKey: VOICE_PREFERENCES_QUERY_KEY, queryFn: getVoicePreferences })

  useEffect(() => {
    if (query.data) {
      useVoicePreferencesStore.setState(query.data)
    }
  }, [query.data])

  return query
}
