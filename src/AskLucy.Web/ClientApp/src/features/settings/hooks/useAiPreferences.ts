import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as aiPreferencesApi from '../api/aiPreferencesApi'
import type { GenerationParameters } from '../../chat/api/aiApi'

const AI_PREFERENCES_QUERY_KEY = ['ai', 'preferences']

export function useAiPreferences() {
  return useQuery({ queryKey: AI_PREFERENCES_QUERY_KEY, queryFn: aiPreferencesApi.getPreferences })
}

export function useSaveAiPreferences() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({
      defaultProviderId,
      defaultModelId,
      defaultGenerationParameters,
    }: {
      defaultProviderId: string
      defaultModelId: string
      defaultGenerationParameters?: GenerationParameters
    }) => aiPreferencesApi.savePreferences(defaultProviderId, defaultModelId, defaultGenerationParameters),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: AI_PREFERENCES_QUERY_KEY }),
  })
}
