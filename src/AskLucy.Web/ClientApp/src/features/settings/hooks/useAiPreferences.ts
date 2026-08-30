import { useQuery } from '@tanstack/react-query'
import * as aiPreferencesApi from '../api/aiPreferencesApi'

const AI_PREFERENCES_QUERY_KEY = ['ai', 'preferences']

/**
 * Read-only. The chat provider/model is an administrator setting now — configured in the admin
 * panel as the Chat capability — so there is nothing here for a user to save. The matching
 * mutation and its PUT endpoint were removed rather than left in place writing a value nothing
 * reads.
 */
export function useAiPreferences() {
  return useQuery({ queryKey: AI_PREFERENCES_QUERY_KEY, queryFn: aiPreferencesApi.getPreferences })
}
